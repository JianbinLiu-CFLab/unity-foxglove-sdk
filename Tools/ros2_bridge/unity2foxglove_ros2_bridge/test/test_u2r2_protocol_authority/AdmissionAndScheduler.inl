      const u2r2::ContractKey other{
        identity.key.contract_id + 1,
        identity.key.generation};
      EXPECT_EQ(
        u2r2::EnqueueDisposition::accepted,
        scheduler.enqueue_data(
          u2r2::OutboundFrame::data(
            "same-data",
            identity.key,
            1,
            Bytes("01")),
          u2r2::QueueOverflowPolicy::reject));
      EXPECT_EQ(
        u2r2::EnqueueDisposition::accepted,
        scheduler.enqueue_data(
          u2r2::OutboundFrame::data(
            "other-data",
            other,
            1,
            Bytes("02")),
          u2r2::QueueOverflowPolicy::reject));
      EXPECT_EQ(
        scenario.at("expectedOrder").get<std::vector<std::string>>(),
        DrainAll(scheduler));
    } else if (id == "reserved_control_survives_full_data_budget") {
      const auto bounded = limits.with({
        {"fixedFrameBytes", 1}, {"maxHeaderBytes", 1}, {"maxPayloadBytes", 8},
        {"maxTransientBytes", 16}, {"maxInFlightBytes", 16},
        {"maxPerContractQueueDepth", 2}, {"maxPerContractQueueBytes", 16},
        {"maxTotalQueueDepth", 3}, {"maxQueuedBytes", 24},
        {"reservedControlQueueDepth", 1}, {"reservedControlQueueBytes", 8},
        {"controlBurstLimit", 1}});
      u2r2::BoundedOutboundScheduler scheduler(bounded);
      const u2r2::ContractKey key{1, 1};
      const auto data_tokens =
        scenario.at("dataTokens").get<std::vector<std::string>>();
      EXPECT_EQ(
        u2r2::EnqueueDisposition::accepted,
        scheduler.enqueue_data(
          u2r2::OutboundFrame::data(data_tokens.at(0), key, 1, std::vector<uint8_t>(8)),
          u2r2::QueueOverflowPolicy::reject));
      EXPECT_EQ(
        u2r2::EnqueueDisposition::accepted,
        scheduler.enqueue_data(
          u2r2::OutboundFrame::data(data_tokens.at(1), key, 2, std::vector<uint8_t>(8)),
          u2r2::QueueOverflowPolicy::reject));
      EXPECT_EQ(
        u2r2::EnqueueDisposition::rejected,
        scheduler.enqueue_data(
          u2r2::OutboundFrame::data("data-overflow", key, 3, Bytes("01")),
          u2r2::QueueOverflowPolicy::reject));
      auto reservation = scheduler.try_reserve_control(8);
      EXPECT_EQ(
        scenario.at("expectedControlReserved").get<bool>(),
        reservation.has_value());
      ASSERT_TRUE(reservation.has_value());
      reservation->commit(u2r2::OutboundFrame::control(
          scenario.at("controlToken").get<std::string>(),
          std::vector<uint8_t>(8)));
    } else if (id == "queued_writer_accounting_exact") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      const u2r2::ContractKey key{1, 1};
      const auto bytes = scenario.at("frameBytes").get<size_t>();
      (void)scheduler.enqueue_data(
        u2r2::OutboundFrame::data("frame", key, 1, std::vector<uint8_t>(bytes)),
        u2r2::QueueOverflowPolicy::reject);
      EXPECT_EQ(
        scenario.at("expectedQueuedBeforeWrite").get<uint64_t>(),
        scheduler.queued_bytes());
      auto writer = scheduler.try_begin_write();
      ASSERT_TRUE(writer.has_value());
      EXPECT_EQ(
        scenario.at("expectedQueuedDuringWrite").get<uint64_t>(),
        scheduler.queued_bytes());
      EXPECT_EQ(
        scenario.at("expectedInFlightDuringWrite").get<uint64_t>(),
        scheduler.in_flight_bytes());
      writer.reset();
      EXPECT_EQ(
        scenario.at("expectedFinalBytes").get<uint64_t>(),
        scheduler.queued_bytes());
      EXPECT_EQ(
        scenario.at("expectedFinalBytes").get<uint64_t>(),
        scheduler.in_flight_bytes());
    } else if (id == "byte_reservations_release_exactly_once") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      auto transient = scheduler.try_reserve_transient(8);
      auto reader = scheduler.try_begin_read(16);
      ASSERT_TRUE(transient.has_value());
      ASSERT_TRUE(reader.has_value());
      transient.reset();
      reader.reset();
      EXPECT_EQ(scenario.at("expectedFinalBytes").get<uint64_t>(), scheduler.transient_bytes());
      EXPECT_EQ(scenario.at("expectedFinalBytes").get<uint64_t>(), scheduler.in_flight_bytes());
    } else if (id == "concurrent_lease_settlement_exactly_once") {
      const auto workers = scenario.at("workerCount").get<size_t>();
      const auto iterations = scenario.at("iterations").get<size_t>();
      u2r2::BoundedOutboundScheduler scheduler(limits);
      auto transient_sentinel = scheduler.try_reserve_transient(3);
      ASSERT_TRUE(transient_sentinel.has_value());
      for (size_t iteration = 0; iteration < iterations; ++iteration) {
        auto transient = scheduler.try_reserve_transient(1);
        ASSERT_TRUE(transient.has_value());
        Race(workers, [&]() {(void)transient->release();});
        ASSERT_EQ(3U, scheduler.transient_bytes());
      }
      transient_sentinel->release();
      EXPECT_EQ(
        scenario.at("expectedFinalBytes").get<uint64_t>(),
        scheduler.transient_bytes());

      auto reader = scheduler.try_begin_read(8);
      ASSERT_TRUE(reader.has_value());
      Race(workers, [&]() {(void)reader->release();});
      EXPECT_EQ(
        scenario.at("expectedFinalBytes").get<uint64_t>(),
        scheduler.in_flight_bytes());

      const u2r2::ContractKey key{1, 1};
      auto in_flight_sentinel = scheduler.try_begin_read(3);
      ASSERT_TRUE(in_flight_sentinel.has_value());
      ASSERT_EQ(
        u2r2::EnqueueDisposition::accepted,
        scheduler.enqueue_data(
          u2r2::OutboundFrame::data(
            "writer", key, 1, std::vector<uint8_t>(8)),
          u2r2::QueueOverflowPolicy::reject));
      auto writer = scheduler.try_begin_write();
      ASSERT_TRUE(writer.has_value());
      Race(workers, [&]() {(void)writer->release();});
      ASSERT_EQ(3U, scheduler.in_flight_bytes());
      in_flight_sentinel->release();
      EXPECT_EQ(
        scenario.at("expectedFinalBytes").get<uint64_t>(),
        scheduler.in_flight_bytes());

      u2r2::SessionResourceAuthority resources(limits);
      auto resource_sentinel =
        resources.try_acquire(u2r2::ConnectionRole::probe);
      ASSERT_TRUE(resource_sentinel.has_value());
      for (size_t iteration = 0; iteration < iterations; ++iteration) {
        auto resource =
          resources.try_acquire(u2r2::ConnectionRole::data_session);
        ASSERT_TRUE(resource.has_value());
        Race(workers, [&]() {(void)resource->release();});
        ASSERT_EQ(1U, resources.connection_count());
      }
      resource_sentinel->release();
      EXPECT_EQ(
        scenario.at("expectedFinalConnections").get<uint64_t>(),
        resources.connection_count());

      for (size_t iteration = 0; iteration < iterations; ++iteration) {
        auto sentinel = scheduler.try_reserve_control(1);
        ASSERT_TRUE(sentinel.has_value());
        sentinel->commit(
          u2r2::OutboundFrame::control("sentinel", Bytes("01")));
        auto control = scheduler.try_reserve_control(1);
        ASSERT_TRUE(control.has_value());
        std::atomic<size_t> commit_wins{0};
        std::atomic<size_t> cancel_wins{0};
        RaceTwo(
          [&]() {
            if (control->try_commit(
                u2r2::OutboundFrame::control("race", Bytes("01"))))
            {
              commit_wins.fetch_add(1);
            }
          },
          [&]() {
            if (control->try_cancel()) {
              cancel_wins.fetch_add(1);
            }
          });
        ASSERT_EQ(1U, commit_wins.load() + cancel_wins.load());
        const auto drained = DrainAll(scheduler);
        EXPECT_EQ(
          1,
          std::count(drained.begin(), drained.end(), "sentinel"));
        EXPECT_EQ(
          commit_wins.load(),
          static_cast<size_t>(
            std::count(drained.begin(), drained.end(), "race")));
        ASSERT_EQ(0U, scheduler.queued_bytes());
        ASSERT_EQ(0U, scheduler.in_flight_bytes());
      }

      auto invalid = scheduler.try_reserve_control(1);
      ASSERT_TRUE(invalid.has_value());
      EXPECT_THROW(
        invalid->commit(
          u2r2::OutboundFrame::data(
            "not-control", key, 2, Bytes("01"))),
        std::invalid_argument);
      EXPECT_TRUE(invalid->try_cancel());
      EXPECT_EQ(
        scenario.at("expectedFinalBytes").get<uint64_t>(),
        scheduler.queued_bytes());

      // Move-assigning over a live lease must settle the destination first.
      auto first_transient = scheduler.try_reserve_transient(2);
      auto second_transient = scheduler.try_reserve_transient(3);
      ASSERT_TRUE(first_transient.has_value());
      ASSERT_TRUE(second_transient.has_value());
      *first_transient = std::move(*second_transient);
      EXPECT_EQ(3U, scheduler.transient_bytes());
      first_transient->release();
      EXPECT_EQ(0U, scheduler.transient_bytes());

      auto first_control = scheduler.try_reserve_control(2);
      auto second_control = scheduler.try_reserve_control(3);
      ASSERT_TRUE(first_control.has_value());
      ASSERT_TRUE(second_control.has_value());
      *first_control = std::move(*second_control);
      EXPECT_EQ(3U, scheduler.queued_bytes());
      first_control->try_cancel();
      EXPECT_EQ(0U, scheduler.queued_bytes());

      u2r2::SessionResourceAuthority other_resources(limits);
      auto first_resource =
        resources.try_acquire(u2r2::ConnectionRole::data_session);
      auto second_resource =
        other_resources.try_acquire(u2r2::ConnectionRole::data_session);
      ASSERT_TRUE(first_resource.has_value());
      ASSERT_TRUE(second_resource.has_value());
      *first_resource = std::move(*second_resource);
      EXPECT_EQ(0U, resources.connection_count());
      EXPECT_EQ(1U, other_resources.connection_count());
      first_resource->release();
      EXPECT_EQ(0U, other_resources.connection_count());

      u2r2::BoundedOutboundScheduler first_writer_scheduler(limits);
      u2r2::BoundedOutboundScheduler second_writer_scheduler(limits);
      EnqueueControl(first_writer_scheduler, "first");
      EnqueueControl(second_writer_scheduler, "second");
      auto first_writer = first_writer_scheduler.try_begin_write();
      auto second_writer = second_writer_scheduler.try_begin_write();
      ASSERT_TRUE(first_writer.has_value());
      ASSERT_TRUE(second_writer.has_value());
      *first_writer = std::move(*second_writer);
      EXPECT_EQ(0U, first_writer_scheduler.in_flight_bytes());
      EXPECT_EQ(1U, second_writer_scheduler.in_flight_bytes());
      first_writer->release();
      EXPECT_EQ(0U, second_writer_scheduler.in_flight_bytes());
    } else if (id == "terminal_close_cancels_pending_authorities") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      auto pending = replay.admit(
        scenario.at("requestId").get<uint64_t>(),
        Bytes("01"),
        1,
        scheduler);
      u2r2::ContractAuthority contracts(limits, DefaultSemanticErrorFrame);
      const auto identity = Identity(Key(scenario));
      auto register_response = replay.admit(
        scenario.at("requestId").get<uint64_t>() + 1,
        RequestBytes("register_subscription", identity),
        1,
        scheduler);
      auto registration = contracts.begin_registration(
        identity, scheduler, replay, register_response);
      const auto second = Identity({
        identity.key.contract_id + 1,
        identity.key.generation});
      auto ready_response = replay.admit(
        scenario.at("requestId").get<uint64_t>() + 2,
        RequestBytes("register_subscription", second),
        1,
        scheduler);
      auto ready = contracts.begin_registration(
        second, scheduler, replay, ready_response);
      contracts.commit_ready(
        ready,
        replay,
        ready_response,
        u2r2::OutboundFrame::control("subscription_ready", Bytes("01")));
      (void)DrainOne(scheduler);
      auto removal_response = replay.admit(
        scenario.at("requestId").get<uint64_t>() + 3,
        RequestBytes("unregister_subscription", second),
        1,
        scheduler);
      auto removal = contracts.begin_unregister(
        second, scheduler, replay, removal_response);

      EXPECT_EQ(3U, replay.outstanding_requests());
      EXPECT_EQ(2U, contracts.contract_count());
      EXPECT_EQ(1U, scheduler.revoked_contract_count());
      for (
        auto call = 0;
        call < scenario.at("closeCalls").get<int>();
        ++call)
      {
        contracts.close(scheduler, replay);
      }
      EXPECT_TRUE(contracts.is_closed());
      EXPECT_TRUE(replay.is_closed());
      EXPECT_TRUE(scheduler.is_closed());
      EXPECT_EQ(
        scenario.at("expectedContracts").get<uint64_t>(),
        contracts.contract_count());
      EXPECT_EQ(
        scenario.at("expectedOutstandingRequests").get<uint64_t>(),
        replay.outstanding_requests());
      EXPECT_EQ(
        scenario.at("expectedRetainedEntries").get<uint64_t>(),
        replay.retained_entries());
      EXPECT_EQ(
        scenario.at("expectedReplayBytes").get<uint64_t>(),
        replay.replay_bytes());
      EXPECT_EQ(
        scenario.at("expectedReservedDepth").get<uint64_t>(),
        scheduler.total_queued_depth());
      EXPECT_EQ(
        scenario.at("expectedReservedBytes").get<uint64_t>(),
        scheduler.queued_bytes());
      EXPECT_EQ(
        scenario.at("expectedRevokedContracts").get<uint64_t>(),
        scheduler.revoked_contract_count());
    } else if (id == "terminal_close_rejects_wrong_authorities") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      u2r2::BoundedOutboundScheduler wrong_scheduler(limits);
      u2r2::RequestReplayAuthority wrong_replay(limits);
      u2r2::ContractAuthority contracts(limits, DefaultSemanticErrorFrame);
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
      auto wrong_response = wrong_replay.admit(
        scenario.at("wrongRequestId").get<uint64_t>(),
        Bytes("bb"),
        1,
        wrong_scheduler);

      EXPECT_THROW(
        contracts.close(wrong_scheduler, wrong_replay),
        std::logic_error);
      EXPECT_FALSE(contracts.is_closed());
      EXPECT_FALSE(replay.is_closed());
      EXPECT_FALSE(scheduler.is_closed());
      EXPECT_FALSE(wrong_replay.is_closed());
      EXPECT_FALSE(wrong_scheduler.is_closed());
      EXPECT_EQ(1U, contracts.contract_count());
      EXPECT_EQ(
        scenario.at("expectedBoundOutstanding").get<uint64_t>(),
        replay.outstanding_requests());
      EXPECT_EQ(
        scenario.at("expectedBoundReservedDepth").get<uint64_t>(),
        scheduler.total_queued_depth());
      EXPECT_EQ(
        scenario.at("expectedBoundOutstanding").get<uint64_t>(),
        wrong_replay.outstanding_requests());
      EXPECT_EQ(
        scenario.at("expectedBoundReservedDepth").get<uint64_t>(),
        wrong_scheduler.total_queued_depth());

      contracts.close(scheduler, replay);
      EXPECT_TRUE(contracts.is_closed());
      EXPECT_TRUE(replay.is_closed());
      EXPECT_TRUE(scheduler.is_closed());
      EXPECT_EQ(0U, contracts.contract_count());
      EXPECT_EQ(
        scenario.at("expectedClosedOutstanding").get<uint64_t>(),
        replay.outstanding_requests());
      EXPECT_EQ(
        scenario.at("expectedClosedReservedDepth").get<uint64_t>(),
        scheduler.total_queued_depth());
      EXPECT_THROW(
        contracts.close(wrong_scheduler, wrong_replay),
        std::logic_error);
      EXPECT_FALSE(wrong_replay.is_closed());
      EXPECT_FALSE(wrong_scheduler.is_closed());

      wrong_replay.cancel_pending(wrong_response);
      EXPECT_EQ(
        scenario.at("expectedClosedOutstanding").get<uint64_t>(),
        wrong_replay.outstanding_requests());
      EXPECT_EQ(
        scenario.at("expectedClosedReservedDepth").get<uint64_t>(),
        wrong_scheduler.total_queued_depth());
    } else if (id == "revoked_capacity_rejection_has_no_side_effects") {
      const auto bounded = limits.with({
        {"maxContracts", scenario.at("maxContracts").get<uint64_t>()},
        {"maxTombstones", scenario.at("maxTombstones").get<uint64_t>()}});
      u2r2::BoundedOutboundScheduler scheduler(bounded);
      const auto expected_bound =
        scenario.at("expectedBound").get<uint64_t>();
      for (
        uint64_t contract_id = 1;
        contract_id <= expected_bound;
        ++contract_id)
      {
        scheduler.revoke_contract({contract_id, 1});
      }
      EXPECT_EQ(expected_bound, scheduler.revoked_contract_count());

      for (
        auto attempt = 0;
        attempt < scenario.at("attackAttempts").get<int>();
        ++attempt)
      {
        const auto contract_id =
          expected_bound + 1U + static_cast<uint64_t>(attempt);
        EXPECT_THROW(
          scheduler.revoke_contract({contract_id, 1}),
          std::logic_error);
        EXPECT_EQ(expected_bound, scheduler.revoked_contract_count());
      }
