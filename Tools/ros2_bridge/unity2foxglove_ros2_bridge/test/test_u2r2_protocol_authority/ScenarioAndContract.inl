      for (const auto & mutation : scenario.at("aliasMutations")) {
        const auto alias = AliasIdentity(
          identity,
          mutation.get<std::string>());
        ExpectProtocolError(scenario, [&]() {
          (void)contracts.admit_message(alias, 1);
        });
      }
      EXPECT_EQ(
        u2r2::MessageAdmission::accepted,
        contracts.admit_message(identity, 1));
    } else if (id == "composed_register_unregister_single_response") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      u2r2::ContractAuthority contracts(limits, DefaultSemanticErrorFrame);
      const u2r2::ContractKey key{
        scenario.at("contractId").get<uint64_t>(),
        scenario.at("generation").get<uint64_t>()};
      const auto identity = Identity(key);
      const auto register_request =
        RequestBytes("register_subscription", identity);
      const auto register_id =
        scenario.at("registerRequestId").get<uint64_t>();
      std::vector<std::string> order;
      auto ready_response = replay.admit(
        register_id, register_request, 1, scheduler);
      auto registration = contracts.begin_registration(
        identity, scheduler, replay, ready_response);
      contracts.commit_ready(
        registration,
        replay,
        ready_response,
        u2r2::OutboundFrame::control("subscription_ready", Bytes("01")));
      EXPECT_EQ(
        scenario.at("expectedRegisterResponses").get<uint64_t>(),
        scheduler.total_queued_depth());
      order.push_back(DrainOne(scheduler).token());

      auto ready_replay = replay.admit(
        register_id, register_request, 1, scheduler);
      auto replayed_registration = contracts.begin_registration(
        identity, scheduler, replay, ready_replay);
      contracts.commit_ready(
        replayed_registration,
        replay,
        ready_replay,
        u2r2::OutboundFrame::control("must-not-send", Bytes("01")));
      EXPECT_EQ(
        scenario.at("expectedReplayResponses").get<uint64_t>(),
        scheduler.total_queued_depth());
      order.push_back(DrainOne(scheduler).token());

      EXPECT_EQ(
        u2r2::MessageAdmission::accepted,
        contracts.admit_message(identity, 1));
      EXPECT_EQ(
        u2r2::EnqueueDisposition::accepted,
        scheduler.enqueue_data(
          u2r2::OutboundFrame::data("message", key, 1, Bytes("01")),
          u2r2::QueueOverflowPolicy::reject));
      order.push_back(DrainOne(scheduler).token());

      const auto unregister_request =
        RequestBytes("unregister_subscription", identity);
      const auto unregister_id =
        scenario.at("unregisterRequestId").get<uint64_t>();
      auto removed_response = replay.admit(
        unregister_id, unregister_request, 1, scheduler);
      auto removal = contracts.begin_unregister(
        identity, scheduler, replay, removed_response);
      EXPECT_TRUE(contracts.try_commit_removed(
          removal,
          scheduler,
          replay,
          removed_response,
          u2r2::OutboundFrame::control(
            "subscription_removed", Bytes("02"))));
      EXPECT_EQ(
        scenario.at("expectedUnregisterResponses").get<uint64_t>(),
        scheduler.total_queued_depth());
      order.push_back(DrainOne(scheduler).token());

      auto removed_replay = replay.admit(
        unregister_id, unregister_request, 1, scheduler);
      auto replayed_removal = contracts.begin_unregister(
        identity, scheduler, replay, removed_replay);
      EXPECT_TRUE(contracts.try_commit_removed(
          replayed_removal,
          scheduler,
          replay,
          removed_replay,
          u2r2::OutboundFrame::control("must-not-send", Bytes("02"))));
      EXPECT_EQ(
        scenario.at("expectedReplayResponses").get<uint64_t>(),
        scheduler.total_queued_depth());
      order.push_back(DrainOne(scheduler).token());
      EXPECT_EQ(
        scenario.at("expectedOrder").get<std::vector<std::string>>(),
        order);
      EXPECT_EQ(0U, scheduler.total_queued_depth());
      EXPECT_EQ(0U, contracts.contract_count());
      EXPECT_EQ(1U, contracts.tombstone_count());
    } else if (id == "fenced_response_fifo_and_transaction_binding") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      u2r2::ContractAuthority contracts(limits, DefaultSemanticErrorFrame);
      const u2r2::ContractKey key{
        scenario.at("contractId").get<uint64_t>(),
        scenario.at("generation").get<uint64_t>()};
      const auto identity = Identity(key);
      auto registration_response = replay.admit(
        scenario.at("registerRequestId").get<uint64_t>(),
        RequestBytes("register_subscription", identity),
        1,
        scheduler);
      auto registration = contracts.begin_registration(
        identity,
        scheduler,
        replay,
        registration_response);
      auto decoy_response = replay.admit(
        scenario.at("decoyRequestId").get<uint64_t>(),
        Bytes("ee"),
        1,
        scheduler);
      EXPECT_THROW(
        contracts.commit_ready(
          registration,
          replay,
          decoy_response,
          u2r2::OutboundFrame::control("wrong", Bytes("ff"))),
        std::logic_error);
      replay.cancel_pending(decoy_response);
      contracts.commit_ready(
        registration,
        replay,
        registration_response,
        u2r2::OutboundFrame::control("subscription_ready", Bytes("01")));

      auto removal_response = replay.admit(
        scenario.at("unregisterRequestId").get<uint64_t>(),
        RequestBytes("unregister_subscription", identity),
        1,
        scheduler);
      auto removal = contracts.begin_unregister(
        identity,
        scheduler,
        replay,
        removal_response);
      EXPECT_TRUE(contracts.try_commit_removed(
          removal,
          scheduler,
          replay,
          removal_response,
          u2r2::OutboundFrame::control(
            "subscription_removed", Bytes("02"))));
      EXPECT_EQ(
        scenario.at("expectedOrder").get<std::vector<std::string>>(),
        DrainAll(scheduler));
      EXPECT_EQ(
        scenario.at("expectedOutstandingRequests").get<uint64_t>(),
        replay.outstanding_requests());
      EXPECT_EQ(0U, contracts.contract_count());
      EXPECT_EQ(1U, contracts.tombstone_count());
    } else if (id == "semantic_rejections_commit_exact_replay") {
      const auto bounded = limits.with({{"maxContracts", 1}});
      u2r2::BoundedOutboundScheduler scheduler(bounded);
      u2r2::RequestReplayAuthority replay(bounded);
      const auto & cases = scenario.at("cases");
      const auto kind_for_request = [&](uint64_t request_id) {
          if (request_id == scenario.at("duplicateRequestId").get<uint64_t>()) {
            return std::string("duplicate");
          }
          if (request_id == scenario.at("capacityRequestId").get<uint64_t>()) {
            return std::string("capacity");
          }
          if (request_id == scenario.at("unknownRequestId").get<uint64_t>()) {
            return std::string("unknown");
          }
          throw std::logic_error("unexpected semantic-rejection request ID");
        };
      const auto case_for_kind = [&](const std::string & kind)
          -> const nlohmann::json & {
          for (const auto & item : cases) {
            if (item.at("kind").get<std::string>() == kind) {
              return item;
            }
          }
          throw std::logic_error("missing semantic-rejection case");
        };
      u2r2::ContractAuthority contracts(
        bounded,
        [&](u2r2::Operation operation,
          uint64_t request_id,
          const u2r2::ProtocolError & error)
        {
          const auto & item = case_for_kind(kind_for_request(request_id));
          EXPECT_EQ(
            item.at("responseOperation").get<std::string>(),
            OperationToken(operation));
          EXPECT_EQ(item.at("errorCode").get<std::string>(), error.code());
          EXPECT_EQ(item.at("terminal").get<bool>(), error.terminal());
          return u2r2::OutboundFrame::control(
            OperationToken(operation) + ":" + std::to_string(request_id) +
            ":" + error.code(),
            Bytes(item.at("responseHex").get<std::string>()));
        });

      const auto identity = Identity(Key(scenario));
      auto initial_response = replay.admit(
        1,
        RequestBytes("register_subscription", identity),
        1,
        scheduler);
      auto initial_registration = contracts.begin_registration(
        identity,
        scheduler,
        replay,
        initial_response);
      contracts.commit_ready(
        initial_registration,
        replay,
        initial_response,
        u2r2::OutboundFrame::control("subscription_ready", Bytes("01")));
      (void)DrainOne(scheduler);

      const auto expect_exact_semantic_replay =
        [&](const std::string & kind,
          uint64_t request_id,
          const std::string & request_operation,
          const u2r2::ContractIdentity & rejected_identity,
          bool registration)
        {
          const auto & item = case_for_kind(kind);
          const auto request = RequestBytes(request_operation, rejected_identity);
          auto response = replay.admit(request_id, request, 1, scheduler);
          try {
            if (registration) {
              (void)contracts.begin_registration(
                rejected_identity,
                scheduler,
                replay,
                response);
            } else {
              (void)contracts.begin_unregister(
                rejected_identity,
                scheduler,
                replay,
                response);
            }
            FAIL() << "expected semantic rejection";
          } catch (const u2r2::ProtocolError & error) {
            EXPECT_EQ(item.at("errorCode").get<std::string>(), error.code());
            EXPECT_EQ(item.at("terminal").get<bool>(), error.terminal());
          }
          const auto first = DrainOne(scheduler);
          EXPECT_EQ(
            Bytes(item.at("responseHex").get<std::string>()),
            first.bytes());
          EXPECT_NE(
            std::string::npos,
            first.token().find(
              item.at("responseOperation").get<std::string>()));

          auto repeated = replay.admit(request_id, request, 1, scheduler);
          EXPECT_EQ(u2r2::ReplayDecision::replay_cached, repeated.decision());
          EXPECT_EQ(
            Bytes(item.at("responseHex").get<std::string>()),
            repeated.cached_response());
          EXPECT_EQ(
            Bytes(item.at("responseHex").get<std::string>()),
            DrainOne(scheduler).bytes());
        };
