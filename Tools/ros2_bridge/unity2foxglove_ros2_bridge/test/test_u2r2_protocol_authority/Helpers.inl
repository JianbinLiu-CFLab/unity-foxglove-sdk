TEST(U2R2ProtocolAuthority, DroppedAdmissionsRollbackEveryOwnedBoundedResource)
{
  const auto limits = u2r2::ProtocolLimits::defaults();
  u2r2::BoundedOutboundScheduler scheduler(limits);
  u2r2::RequestReplayAuthority replay(limits);
  {
    auto response = replay.admit(1, Bytes("01"), 1, scheduler);
  }
  EXPECT_EQ(0U, replay.outstanding_requests());
  EXPECT_EQ(0U, replay.replay_bytes());
  EXPECT_EQ(0U, scheduler.total_queued_depth());

  u2r2::ContractAuthority contracts(limits, DefaultSemanticErrorFrame);
  const auto identity = Identity({41, 7});
  auto register_response = replay.admit(
    2, RequestBytes("register_subscription", identity), 1, scheduler);
  {
    auto registration = contracts.begin_registration(
      identity, scheduler, replay, register_response);
  }
  EXPECT_EQ(0U, contracts.contract_count());
  EXPECT_EQ(0U, replay.outstanding_requests());
  EXPECT_EQ(0U, scheduler.total_queued_depth());

  auto ready_response = replay.admit(
    3, RequestBytes("register_subscription", identity), 1, scheduler);
  {
    auto registration = contracts.begin_registration(
      identity, scheduler, replay, ready_response);
    contracts.commit_ready(
      registration,
      replay,
      ready_response,
      u2r2::OutboundFrame::control("subscription_ready", Bytes("01")));
  }
  (void)DrainOne(scheduler);

  auto remove_response = replay.admit(
    4, RequestBytes("unregister_subscription", identity), 1, scheduler);
  {
    auto removal = contracts.begin_unregister(
      identity, scheduler, replay, remove_response);
  }
  EXPECT_EQ(0U, contracts.contract_count());
  EXPECT_EQ(0U, replay.outstanding_requests());
  EXPECT_EQ(0U, scheduler.total_queued_depth());
}

TEST(U2R2ProtocolAuthority, SharedCommit2LedgerDrivesEveryBoundedAuthorityScenario)
{
  const auto authority_json = LoadAuthority();
  const auto limits_json = authority_json.at("limits");
  const auto limits = LimitsFrom(limits_json);
  const auto & scenarios = authority_json.at("scenarios");
  ASSERT_EQ(58U, scenarios.size());

  std::unordered_set<std::string> consumed;
  for (const auto & scenario : scenarios) {
    const auto id = scenario.at("id").get<std::string>();
    SCOPED_TRACE(id);
    ASSERT_TRUE(consumed.insert(id).second) << id;

    if (id == "sender_starts_at_one") {
      u2r2::RequestIdCounter counter;
      EXPECT_EQ(scenario.at("expectedIds").at(0).get<uint64_t>(), counter.next());
      EXPECT_EQ(scenario.at("expectedIds").at(1).get<uint64_t>(), counter.next());
    } else if (id == "receiver_accepts_higher_first") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      auto admission = replay.admit(
        scenario.at("requestId").get<uint64_t>(), Bytes("01"), 1, scheduler);
      EXPECT_EQ(u2r2::ReplayDecision::begin_mutation, admission.decision());
      replay.complete(admission, Bytes("aa"));
      EXPECT_EQ(scenario.at("expectedHighWater").get<uint64_t>(), replay.high_water_mark());
    } else if (id == "retained_identical_replay") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      const auto request = Bytes(scenario.at("canonicalRequestHex").get<std::string>());
      const auto response = Bytes(scenario.at("responseHex").get<std::string>());
      auto first = replay.admit(
        scenario.at("requestId").get<uint64_t>(), request, response.size(), scheduler);
      replay.complete(first, response);
      (void)DrainOne(scheduler);
      auto repeated = replay.admit(
        scenario.at("requestId").get<uint64_t>(), request, response.size(), scheduler);
      EXPECT_EQ(u2r2::ReplayDecision::replay_cached, repeated.decision());
      EXPECT_EQ(response, repeated.cached_response());
      EXPECT_EQ(response, DrainOne(scheduler).bytes());
    } else if (id == "retained_payload_conflict") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      const auto request_id = scenario.at("requestId").get<uint64_t>();
      auto first = replay.admit(
        request_id, Bytes(scenario.at("canonicalRequestHex").get<std::string>()), 1, scheduler);
      replay.complete(first, Bytes("aa"));
      (void)DrainOne(scheduler);
      ExpectProtocolError(scenario, [&]() {
        (void)replay.admit(
          request_id,
          Bytes(scenario.at("conflictingRequestHex").get<std::string>()),
          1,
          scheduler);
      });
    } else if (id == "stale_after_replay_eviction") {
      const auto bounded = limits.with({
        {"maxOutstandingRequests", 2}, {"maxReplayEntries", 2},
        {"maxReplayBytes", 64},
        {"reservedControlQueueDepth", 4}, {"reservedControlQueueBytes", 64}});
      u2r2::BoundedOutboundScheduler scheduler(bounded);
      u2r2::RequestReplayAuthority replay(bounded);
      for (const auto & value : scenario.at("requestIds")) {
        const auto request_id = value.get<uint64_t>();
        auto admission = replay.admit(
          request_id, {static_cast<uint8_t>(request_id)}, 1, scheduler);
        replay.complete(admission, {static_cast<uint8_t>(request_id + 10)});
        (void)DrainOne(scheduler);
      }
      ExpectProtocolError(scenario, [&]() {
        (void)replay.admit(
          scenario.at("staleRequestId").get<uint64_t>(), Bytes("01"), 1, scheduler);
      });
      auto next = replay.admit(4, Bytes("04"), 1, scheduler);
      EXPECT_EQ(u2r2::ReplayDecision::begin_mutation, next.decision());
    } else if (id == "control_reserved_before_mutation") {
      const auto bounded = limits.with({
        {"reservedControlQueueDepth", 1}, {"reservedControlQueueBytes", 8},
        {"controlBurstLimit", 1}});
      u2r2::BoundedOutboundScheduler scheduler(bounded);
      auto occupied = scheduler.try_reserve_control(8);
      ASSERT_TRUE(occupied.has_value());
      occupied->commit(u2r2::OutboundFrame::control("occupied", std::vector<uint8_t>(8)));
      u2r2::RequestReplayAuthority replay(bounded);
      ExpectProtocolError(scenario, [&]() {
        (void)replay.admit(
          scenario.at("requestId").get<uint64_t>(), Bytes("01"), 1, scheduler);
      });
      EXPECT_EQ(0U, replay.high_water_mark());
      EXPECT_EQ(0U, replay.outstanding_requests());
    } else if (id == "replay_bytes_max_plus_one") {
      const auto bounded = limits.with({{"maxReplayBytes", 8}});
      u2r2::BoundedOutboundScheduler scheduler(bounded);
      u2r2::RequestReplayAuthority replay(bounded);
      ExpectProtocolError(scenario, [&]() {
        (void)replay.admit(
          scenario.at("requestId").get<uint64_t>(),
          std::vector<uint8_t>(4), 5, scheduler);
      });
      EXPECT_EQ(0U, replay.high_water_mark());
    } else if (id == "ready_precedes_message") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::ContractAuthority contracts(limits, DefaultSemanticErrorFrame);
      u2r2::RequestReplayAuthority replay(limits);
      const auto key = Key(scenario);
      const auto identity = Identity(key);
      auto ready_response = replay.admit(
        1, RequestBytes("register_subscription", identity), 1, scheduler);
      auto registration = contracts.begin_registration(
        identity, scheduler, replay, ready_response);
      ExpectProtocolError(scenario, [&]() {
        (void)contracts.admit_message(
          identity,
          scenario.at("firstSequence").get<uint64_t>());
      });
      contracts.commit_ready(
        registration,
        replay,
        ready_response,
        u2r2::OutboundFrame::control("subscription_ready", Bytes("01")));
      EXPECT_EQ(
        u2r2::MessageAdmission::accepted,
        contracts.admit_message(
          identity,
          scenario.at("firstSequence").get<uint64_t>()));
      (void)scheduler.enqueue_data(
        u2r2::OutboundFrame::data("message", key, 1, Bytes("02")),
        u2r2::QueueOverflowPolicy::reject);
      EXPECT_EQ(
        scenario.at("expectedOrder").get<std::vector<std::string>>(),
        DrainAll(scheduler));
    } else if (id == "unregister_fences_writer") {
      const auto key = Key(scenario);
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::ContractAuthority contracts(limits, DefaultSemanticErrorFrame);
      u2r2::RequestReplayAuthority replay(limits);
      const auto identity = Identity(key);
      auto ready_response = replay.admit(
        1, RequestBytes("register_subscription", identity), 1, scheduler);
      auto registration = contracts.begin_registration(
        identity, scheduler, replay, ready_response);
      contracts.commit_ready(
        registration,
        replay,
        ready_response,
        u2r2::OutboundFrame::control("subscription_ready", Bytes("01")));
      std::vector<std::string> order{DrainOne(scheduler).token()};
      EXPECT_EQ(
        u2r2::EnqueueDisposition::accepted,
        scheduler.enqueue_data(
          u2r2::OutboundFrame::data("message", key, 1, Bytes("aa")),
          u2r2::QueueOverflowPolicy::reject));
      auto writer = scheduler.try_begin_write();
      ASSERT_TRUE(writer.has_value());
      order.push_back(writer->frame().token());
      auto removed_response = replay.admit(
        2, RequestBytes("unregister_subscription", identity), 1, scheduler);
      auto removal = contracts.begin_unregister(
        identity, scheduler, replay, removed_response);
      EXPECT_FALSE(contracts.try_commit_removed(
          removal,
          scheduler,
          replay,
          removed_response,
          u2r2::OutboundFrame::control("subscription_removed", Bytes("02"))));
      EXPECT_EQ(
        u2r2::EnqueueDisposition::rejected,
        scheduler.enqueue_data(
          u2r2::OutboundFrame::data("late", key, 2, Bytes("bb")),
          u2r2::QueueOverflowPolicy::reject));
      writer.reset();
      EXPECT_TRUE(contracts.try_commit_removed(
          removal,
          scheduler,
          replay,
          removed_response,
          u2r2::OutboundFrame::control("subscription_removed", Bytes("02"))));
      order.push_back(DrainOne(scheduler).token());
      EXPECT_EQ(scenario.at("expectedOrder").get<std::vector<std::string>>(), order);
      EXPECT_EQ(
        u2r2::MessageAdmission::late_tombstone,
        contracts.admit_message(identity, 2));
    } else if (id == "bounded_generation_tombstones") {
      const auto bounded = limits.with({{"maxTombstones", 1}});
      u2r2::BoundedOutboundScheduler scheduler(bounded);
      u2r2::RequestReplayAuthority replay(bounded);
      u2r2::ContractAuthority contracts(bounded, DefaultSemanticErrorFrame);
      const u2r2::ContractKey first{
        scenario.at("firstContractId").get<uint64_t>(),
        scenario.at("generation").get<uint64_t>()};
      const u2r2::ContractKey second{
        scenario.at("secondContractId").get<uint64_t>(),
        scenario.at("generation").get<uint64_t>()};
      RegisterAndRemove(contracts, scheduler, replay, first, 1);
      RegisterAndRemove(contracts, scheduler, replay, second, 3);
      EXPECT_EQ(1U, contracts.tombstone_count());
      EXPECT_EQ(
        scenario.at("expectedRevokedContracts").get<uint64_t>(),
        scheduler.revoked_contract_count());
      ExpectProtocolError(scenario, [&]() {
        (void)contracts.admit_message(Identity(first), 1);
      });
      EXPECT_EQ(
        u2r2::MessageAdmission::late_tombstone,
        contracts.admit_message(Identity(second), 1));
    } else if (id == "unknown_contract_faults") {
      u2r2::ContractAuthority contracts(limits, DefaultSemanticErrorFrame);
      ExpectProtocolError(scenario, [&]() {
        (void)contracts.admit_message(Identity(Key(scenario)), 1);
      });
    } else if (id == "sequence_starts_one_and_is_monotonic") {
      u2r2::ContractSequence sequence;
      for (const auto & value : scenario.at("acceptedSequences")) {
        sequence.admit(value.get<uint64_t>());
      }
      EXPECT_EQ(2U, sequence.last_accepted());
      ExpectProtocolError(scenario, [&]() {
        sequence.admit(scenario.at("rejectedSequence").get<uint64_t>());
      });
    } else if (id == "sequence_faults_before_wrap") {
      u2r2::ContractSequence sequence(ParseU64(scenario.at("startingSequence")));
      sequence.admit(ParseU64(scenario.at("lastAcceptedSequence")));
      ExpectProtocolError(scenario, [&]() {sequence.admit(0);});
      EXPECT_TRUE(sequence.is_faulted());
    } else if (
      id == "drop_oldest_is_contract_local" ||
      id == "replace_latest_is_contract_local")
    {
      const auto bounded = limits.with({
        {"fixedFrameBytes", 1}, {"maxHeaderBytes", 1}, {"maxPayloadBytes", 8},
        {"maxTransientBytes", 16}, {"maxInFlightBytes", 16},
        {"maxPerContractQueueDepth", 2}, {"maxPerContractQueueBytes", 16},
        {"maxTotalQueueDepth", 8}, {"maxQueuedBytes", 128},
        {"reservedControlQueueDepth", 2}, {"reservedControlQueueBytes", 16}});
      u2r2::BoundedOutboundScheduler scheduler(bounded);
      const u2r2::ContractKey cold{1, 1};
      const u2r2::ContractKey hot{2, 1};
      const auto policy = id == "drop_oldest_is_contract_local"
        ? u2r2::QueueOverflowPolicy::drop_oldest
        : u2r2::QueueOverflowPolicy::replace_latest;
      (void)scheduler.enqueue_data(
        u2r2::OutboundFrame::data("cold-1", cold, 1, Bytes("01")),
        u2r2::QueueOverflowPolicy::reject);
      (void)scheduler.enqueue_data(
        u2r2::OutboundFrame::data("hot-1", hot, 1, Bytes("02")), policy);
      (void)scheduler.enqueue_data(
        u2r2::OutboundFrame::data("hot-2", hot, 2, Bytes("03")), policy);
      const auto disposition = scheduler.enqueue_data(
        u2r2::OutboundFrame::data("hot-3", hot, 3, Bytes("04")), policy);
      EXPECT_EQ(
        id == "drop_oldest_is_contract_local"
          ? u2r2::EnqueueDisposition::dropped_oldest
          : u2r2::EnqueueDisposition::replaced_latest,
        disposition);
      const auto drained = DrainAll(scheduler);
      EXPECT_NE(drained.end(), std::find(drained.begin(), drained.end(), "cold-1"));
      std::vector<std::string> hot_values;
      for (const auto & token : drained) {
        if (token.rfind("hot-", 0) == 0) {
          hot_values.push_back(token);
        }
      }
      EXPECT_EQ(scenario.at("retainedHotTokens").get<std::vector<std::string>>(), hot_values);
    } else if (id == "zero_byte_replace_releases_depth") {
      u2r2::BoundedOutboundScheduler scheduler(
        limits.with({{"maxPerContractQueueDepth", 1}}));
      const u2r2::ContractKey key{1, 1};
      EXPECT_EQ(
        u2r2::EnqueueDisposition::accepted,
        scheduler.enqueue_data(
          u2r2::OutboundFrame::data(
            "zero-byte-victim",
            key,
            1,
            {}),
          u2r2::QueueOverflowPolicy::reject));
      EXPECT_EQ(
        u2r2::EnqueueDisposition::replaced_latest,
        scheduler.enqueue_data(
          u2r2::OutboundFrame::data(
            "replacement",
            key,
            2,
            Bytes("01")),
          u2r2::QueueOverflowPolicy::replace_latest));
      EXPECT_EQ(
        scenario.at("expectedQueuedDepth").get<uint64_t>(),
        scheduler.data_queued_depth());
      EXPECT_EQ(
        scenario.at("expectedQueuedBytes").get<uint64_t>(),
        scheduler.queued_bytes());
      EXPECT_EQ(
        scenario.at("expectedOrder").get<std::vector<std::string>>(),
        DrainAll(scheduler));
    } else if (id == "per_contract_fifo_round_robin") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      const u2r2::ContractKey a{1, 1};
      const u2r2::ContractKey b{2, 1};
      (void)scheduler.enqueue_data(u2r2::OutboundFrame::data("a-1", a, 1, Bytes("01")), u2r2::QueueOverflowPolicy::reject);
      (void)scheduler.enqueue_data(u2r2::OutboundFrame::data("a-2", a, 2, Bytes("02")), u2r2::QueueOverflowPolicy::reject);
      (void)scheduler.enqueue_data(u2r2::OutboundFrame::data("b-1", b, 1, Bytes("03")), u2r2::QueueOverflowPolicy::reject);
      (void)scheduler.enqueue_data(u2r2::OutboundFrame::data("b-2", b, 2, Bytes("04")), u2r2::QueueOverflowPolicy::reject);
      EXPECT_EQ(scenario.at("expectedOrder").get<std::vector<std::string>>(), DrainAll(scheduler));
    } else if (id == "bounded_control_priority_allows_data") {
      u2r2::BoundedOutboundScheduler scheduler(limits.with({{"controlBurstLimit", 2}}));
      const u2r2::ContractKey key{1, 1};
      (void)scheduler.enqueue_data(
        u2r2::OutboundFrame::data("data-1", key, 1, Bytes("01")),
        u2r2::QueueOverflowPolicy::reject);
      EnqueueControl(scheduler, "control-1");
      EnqueueControl(scheduler, "control-2");
      EnqueueControl(scheduler, "control-3");
      EXPECT_EQ(scenario.at("expectedOrder").get<std::vector<std::string>>(), DrainAll(scheduler));
    } else if (id == "fenced_control_yields_to_other_contract_data") {
      const auto bounded = limits.with({{"controlBurstLimit", 1}});
      u2r2::BoundedOutboundScheduler scheduler(bounded);
      EnqueueControl(scheduler, "control-prime");
      EXPECT_EQ("control-prime", DrainOne(scheduler).token());

      u2r2::RequestReplayAuthority replay(bounded);
      u2r2::ContractAuthority contracts(bounded, DefaultSemanticErrorFrame);
      const auto identity = Identity(Key(scenario));
      auto response = replay.admit(
        scenario.at("requestId").get<uint64_t>(),
        RequestBytes("register_subscription", identity),
        1,
        scheduler);
      auto registration = contracts.begin_registration(
        identity,
        scheduler,
        replay,
        response);
      contracts.commit_ready(
        registration,
        replay,
        response,
        u2r2::OutboundFrame::control(
          "subscription_ready",
          Bytes("01")));
