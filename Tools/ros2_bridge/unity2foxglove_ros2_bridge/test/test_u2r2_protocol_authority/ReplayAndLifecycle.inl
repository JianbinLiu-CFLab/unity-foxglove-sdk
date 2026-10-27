      scheduler.revoke_contract({1, 1});
      EXPECT_EQ(expected_bound, scheduler.revoked_contract_count());
    } else if (id == "unregister_revoked_capacity_is_atomic") {
      const auto bounded = limits.with({
        {"maxContracts", scenario.at("maxContracts").get<uint64_t>()},
        {"maxTombstones", scenario.at("maxTombstones").get<uint64_t>()}});
      u2r2::BoundedOutboundScheduler scheduler(bounded);
      u2r2::RequestReplayAuthority replay(bounded);
      u2r2::ContractAuthority contracts(bounded, DefaultSemanticErrorFrame);
      const auto generation = scenario.at("generation").get<uint64_t>();
      const auto target = Identity({
        scenario.at("targetContractId").get<uint64_t>(),
        generation});
      const auto filler = Identity({
        scenario.at("fillerContractId").get<uint64_t>(),
        generation});
      RegisterReady(
        contracts,
        scheduler,
        replay,
        target,
        scenario.at("targetRegisterRequestId").get<uint64_t>());
      RegisterReady(
        contracts,
        scheduler,
        replay,
        filler,
        scenario.at("fillerRegisterRequestId").get<uint64_t>());

      EXPECT_EQ(
        u2r2::EnqueueDisposition::accepted,
        scheduler.enqueue_data(
          u2r2::OutboundFrame::data(
            "filler",
            filler.key,
            1,
            Bytes("01")),
          u2r2::QueueOverflowPolicy::reject));
      auto writer = scheduler.try_begin_write();
      ASSERT_TRUE(writer.has_value());
      auto filler_response = replay.admit(
        scenario.at("fillerUnregisterRequestId").get<uint64_t>(),
        RequestBytes("unregister_subscription", filler),
        1,
        scheduler);
      auto filler_removal = contracts.begin_unregister(
        filler,
        scheduler,
        replay,
        filler_response);
      contracts.cancel_removal(
        filler_removal,
        scheduler,
        replay,
        filler_response);

      for (const auto & value : scenario.at("revokedFillerIds")) {
        scheduler.revoke_contract({
          value.get<uint64_t>(),
          generation});
      }
      EXPECT_EQ(
        scenario.at("expectedRevokedAtCapacity").get<uint64_t>(),
        scheduler.revoked_contract_count());

      auto failed_response = replay.admit(
        scenario.at("failedUnregisterRequestId").get<uint64_t>(),
        RequestBytes("unregister_subscription", target),
        1,
        scheduler);
      EXPECT_THROW(
        (void)contracts.begin_unregister(
          target,
          scheduler,
          replay,
          failed_response),
        std::logic_error);
      EXPECT_EQ(
        scenario.at("expectedContractsAfterFailure").get<uint64_t>(),
        contracts.contract_count());
      EXPECT_EQ(
        u2r2::MessageAdmission::accepted,
        contracts.admit_message(target, 1));
      replay.cancel_pending(failed_response);
      EXPECT_EQ(
        scenario.at("expectedOutstandingAfterCancel").get<uint64_t>(),
        replay.outstanding_requests());
      EXPECT_EQ(
        scenario.at("expectedReservedDepthAfterCancel").get<uint64_t>(),
        scheduler.total_queued_depth());

      writer.reset();
      EXPECT_EQ(
        scenario.at("expectedRevokedAfterRelease").get<uint64_t>(),
        scheduler.revoked_contract_count());
      auto retry_response = replay.admit(
        scenario.at("retryUnregisterRequestId").get<uint64_t>(),
        RequestBytes("unregister_subscription", target),
        1,
        scheduler);
      auto retry_removal = contracts.begin_unregister(
        target,
        scheduler,
        replay,
        retry_response);
      EXPECT_TRUE(contracts.try_commit_removed(
          retry_removal,
          scheduler,
          replay,
          retry_response,
          u2r2::OutboundFrame::control(
            "subscription_removed",
            Bytes("02"))));
      (void)DrainOne(scheduler);
      EXPECT_EQ(0U, contracts.contract_count());
      EXPECT_EQ(0U, replay.outstanding_requests());
      contracts.close(scheduler, replay);
    } else if (id == "one_reader_and_one_writer") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      auto reader = scheduler.try_begin_read(1);
      ASSERT_TRUE(reader.has_value());
      EXPECT_FALSE(scheduler.try_begin_read(1).has_value());
      EnqueueControl(scheduler, "control");
      auto writer = scheduler.try_begin_write();
      ASSERT_TRUE(writer.has_value());
      EXPECT_FALSE(scheduler.try_begin_write().has_value());
    } else if (id == "capacity_counter_max_plus_one") {
      u2r2::CapacityCounter counter(scenario.at("capacity").get<uint64_t>());
      EXPECT_TRUE(counter.try_acquire());
      EXPECT_TRUE(counter.try_acquire());
      EXPECT_FALSE(counter.try_acquire());
      counter.release();
      counter.release();
      EXPECT_EQ(scenario.at("expectedFinalCount").get<uint64_t>(), counter.count());
      EXPECT_THROW(counter.release(), std::logic_error);
    } else if (id == "checked_frame_size_bounds") {
      const auto maximum = u2r2::FrameSize::create(
        limits, limits.max_header_bytes(), limits.max_payload_bytes());
      EXPECT_EQ(
        limits.fixed_frame_bytes() + limits.max_header_bytes() + limits.max_payload_bytes(),
        maximum.total_bytes());
      EXPECT_GE(limits.max_per_contract_queue_bytes(), maximum.total_bytes());
      EXPECT_GE(limits.max_queued_bytes(), maximum.total_bytes());
      EXPECT_GE(limits.max_transient_bytes(), maximum.total_bytes());
      EXPECT_GE(limits.max_in_flight_bytes(), maximum.total_bytes());
      ExpectProtocolError(scenario, [&]() {
        (void)u2r2::FrameSize::create(
          limits, limits.max_header_bytes(), limits.max_payload_bytes() + 1);
      });
      ExpectProtocolError(scenario, [&]() {
        (void)u2r2::checked_add(
          std::numeric_limits<uint64_t>::max(), 1,
          std::numeric_limits<uint64_t>::max(), "overflow");
      });
    } else if (id == "request_counter_exhausts_before_wrap") {
      u2r2::RequestIdCounter counter(ParseU64(scenario.at("startingRequestId")));
      EXPECT_EQ(ParseU64(scenario.at("lastRequestId")), counter.next());
      ExpectProtocolError(scenario, [&]() {(void)counter.next();});
      EXPECT_TRUE(counter.is_faulted());
    } else if (id == "request_high_water_faults_before_saturation") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      ExpectProtocolError(scenario, [&]() {
        (void)replay.admit(
          ParseU64(scenario.at("requestId")), Bytes("01"), 1, scheduler);
      });
      EXPECT_EQ(
        scenario.at("expectedHighWater").get<uint64_t>(),
        replay.high_water_mark());
      EXPECT_EQ(0U, replay.outstanding_requests());
    } else if (id == "request_counter_is_thread_safe") {
      const auto workers = scenario.at("workerCount").get<size_t>();
      const auto iterations = scenario.at("iterationsPerWorker").get<size_t>();
      const auto expected_unique = scenario.at("expectedUniqueIds").get<size_t>();
      u2r2::RequestIdCounter counter;
      std::vector<uint64_t> ids(expected_unique);
      std::atomic<size_t> next_index{0};
      Race(workers, [&]() {
        for (size_t iteration = 0; iteration < iterations; ++iteration) {
          const auto index = next_index.fetch_add(1);
          ids.at(index) = counter.next();
        }
      });
      EXPECT_EQ(expected_unique, next_index.load());
      std::sort(ids.begin(), ids.end());
      EXPECT_EQ(scenario.at("expectedFirstId").get<uint64_t>(), ids.front());
      EXPECT_EQ(scenario.at("expectedLastId").get<uint64_t>(), ids.back());
      EXPECT_EQ(
        ids.end(),
        std::adjacent_find(ids.begin(), ids.end()));
    } else if (id == "wrong_generation_is_not_a_tombstone") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      u2r2::ContractAuthority contracts(limits, DefaultSemanticErrorFrame);
      const u2r2::ContractKey removed{
        scenario.at("contractId").get<uint64_t>(),
        scenario.at("removedGeneration").get<uint64_t>()};
      RegisterAndRemove(contracts, scheduler, replay, removed, 1);
      const u2r2::ContractKey wrong{
        scenario.at("contractId").get<uint64_t>(),
        scenario.at("wrongGeneration").get<uint64_t>()};
      ExpectProtocolError(scenario, [&]() {
        (void)contracts.admit_message(Identity(wrong), 1);
      });
    } else if (id == "failed_reservation_has_no_side_effects") {
      const auto bounded = limits.with({
        {"reservedControlQueueDepth", 1}, {"reservedControlQueueBytes", 1},
        {"controlBurstLimit", 1}});
      u2r2::BoundedOutboundScheduler scheduler(bounded);
      EnqueueControl(scheduler, "occupied");
      u2r2::RequestReplayAuthority replay(bounded);
      size_t mutations = 0;
      ExpectProtocolError(scenario, [&]() {
        auto admission = replay.admit(
          scenario.at("requestId").get<uint64_t>(), Bytes("01"), 1, scheduler);
        if (admission.decision() == u2r2::ReplayDecision::begin_mutation) {
          ++mutations;
        }
      });
      EXPECT_EQ(scenario.at("expectedHighWater").get<uint64_t>(), replay.high_water_mark());
      EXPECT_EQ(scenario.at("expectedMutationCount").get<size_t>(), mutations);
    } else if (id == "replay_advances_high_water_once") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      const auto request = Bytes(scenario.at("canonicalRequestHex").get<std::string>());
      const auto response = Bytes(scenario.at("responseHex").get<std::string>());
      size_t mutations = 0;
      auto first = replay.admit(
        scenario.at("requestId").get<uint64_t>(), request, response.size(), scheduler);
      if (first.decision() == u2r2::ReplayDecision::begin_mutation) {
        ++mutations;
      }
      replay.complete(first, response);
      (void)DrainOne(scheduler);
      auto repeated = replay.admit(
        scenario.at("requestId").get<uint64_t>(), request, response.size(), scheduler);
      EXPECT_EQ(u2r2::ReplayDecision::replay_cached, repeated.decision());
      EXPECT_EQ(response, repeated.cached_response());
      EXPECT_EQ(scenario.at("expectedHighWater").get<uint64_t>(), replay.high_water_mark());
      EXPECT_EQ(scenario.at("expectedMutationCount").get<size_t>(), mutations);
    } else if (id == "pending_request_identity_is_atomic") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      const auto request = Bytes(scenario.at("canonicalRequestHex").get<std::string>());
      auto pending = replay.admit(
        scenario.at("requestId").get<uint64_t>(), request, 3, scheduler);
      EXPECT_EQ(u2r2::ReplayDecision::begin_mutation, pending.decision());
      EXPECT_EQ(scenario.at("expectedHighWater").get<uint64_t>(), replay.high_water_mark());
      ExpectProtocolError(scenario.at("identicalPending"), [&]() {
        (void)replay.admit(
          scenario.at("requestId").get<uint64_t>(), request, 3, scheduler);
      });
      ExpectProtocolError(scenario.at("conflictingPending"), [&]() {
        (void)replay.admit(
          scenario.at("requestId").get<uint64_t>(),
          Bytes(scenario.at("conflictingRequestHex").get<std::string>()),
          3,
          scheduler);
      });
      ExpectProtocolError(scenario.at("lowerPending"), [&]() {
        (void)replay.admit(
          scenario.at("lowerRequestId").get<uint64_t>(), Bytes("06"), 1, scheduler);
      });
      auto higher = replay.admit(
        scenario.at("higherRequestId").get<uint64_t>(), Bytes("08"), 1, scheduler);
      EXPECT_EQ(u2r2::ReplayDecision::begin_mutation, higher.decision());
    } else if (id == "replay_completion_abort_exactly_once") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      const auto request = Bytes(scenario.at("canonicalRequestHex").get<std::string>());
      const auto complete_response =
        Bytes(scenario.at("completeResponseHex").get<std::string>());
      const auto abort_response =
        Bytes(scenario.at("abortResponseHex").get<std::string>());
      size_t mutations = 0;
      auto completed = replay.admit(
        scenario.at("completeRequestId").get<uint64_t>(),
        request, complete_response.size(), scheduler);
      ++mutations;
      replay.complete(completed, complete_response);
      EXPECT_THROW(replay.complete(completed, complete_response), std::logic_error);
      (void)DrainOne(scheduler);
      auto aborted = replay.admit(
        scenario.at("abortRequestId").get<uint64_t>(),
        request, abort_response.size(), scheduler);
      ++mutations;
      replay.abort(aborted, abort_response);
      EXPECT_THROW(replay.abort(aborted, abort_response), std::logic_error);
      (void)DrainOne(scheduler);
      EXPECT_EQ(
        scenario.at("expectedOutstandingRequests").get<uint64_t>(),
        replay.outstanding_requests());
      EXPECT_EQ(scenario.at("expectedMutationCount").get<size_t>(), mutations);
      auto replayed_abort = replay.admit(
        scenario.at("abortRequestId").get<uint64_t>(),
        request, abort_response.size(), scheduler);
      EXPECT_EQ(u2r2::ReplayDecision::replay_cached, replayed_abort.decision());
      EXPECT_EQ(abort_response, replayed_abort.cached_response());
    } else if (id == "all_named_counters_are_bounded") {
      const std::unordered_set<std::string> expected_names{
        "connections", "dataSessions", "probes", "contracts",
        "outstandingRequests"};
      for (const auto & name : scenario.at("counterNames")) {
        EXPECT_NE(expected_names.end(), expected_names.find(name.get<std::string>()));
      }
      u2r2::SessionResourceAuthority resources(limits);
      std::vector<u2r2::ResourceLease> leases;
      auto data = resources.try_acquire(u2r2::ConnectionRole::data_session);
      ASSERT_TRUE(data.has_value());
      leases.push_back(std::move(*data));
      for (uint64_t index = 0; index < limits.max_probes(); ++index) {
        auto probe = resources.try_acquire(u2r2::ConnectionRole::probe);
        ASSERT_TRUE(probe.has_value());
        leases.push_back(std::move(*probe));
      }
      EXPECT_FALSE(
        resources.try_acquire(u2r2::ConnectionRole::data_session).has_value());
      EXPECT_FALSE(resources.try_acquire(u2r2::ConnectionRole::probe).has_value());
      EXPECT_EQ(limits.max_connections(), resources.connection_count());
      leases.clear();
      EXPECT_EQ(0U, resources.connection_count());

      const auto contract_limits = limits.with({
        {"maxContracts", 2}, {"reservedControlQueueDepth", 3}});
      u2r2::BoundedOutboundScheduler contract_scheduler(contract_limits);
      u2r2::ContractAuthority contracts(
        contract_limits,
        DefaultSemanticErrorFrame);
      u2r2::RequestReplayAuthority contract_replay(contract_limits);
      const auto first_identity = Identity({1, 1});
      auto first_response = contract_replay.admit(
        1,
        RequestBytes("register_subscription", first_identity),
        1,
        contract_scheduler);
      auto first_registration = contracts.begin_registration(
        first_identity,
        contract_scheduler,
        contract_replay,
        first_response);
      contracts.commit_ready(
        first_registration,
        contract_replay,
        first_response,
        u2r2::OutboundFrame::control("subscription_ready:1", Bytes("01")));
      (void)DrainOne(contract_scheduler);
      const auto second_identity = Identity({2, 1});
      auto second_response = contract_replay.admit(
        2,
        RequestBytes("register_subscription", second_identity),
        1,
        contract_scheduler);
      auto second_registration = contracts.begin_registration(
        second_identity,
        contract_scheduler,
        contract_replay,
        second_response);
      contracts.commit_ready(
        second_registration,
        contract_replay,
        second_response,
        u2r2::OutboundFrame::control("subscription_ready:2", Bytes("01")));
      (void)DrainOne(contract_scheduler);
      ExpectProtocolError(scenario, [&]() {
        const auto third_identity = Identity({3, 1});
        auto third_response = contract_replay.admit(
          3,
          RequestBytes("register_subscription", third_identity),
          1,
          contract_scheduler);
        (void)contracts.begin_registration(
          third_identity,
          contract_scheduler,
          contract_replay,
          third_response);
      });
