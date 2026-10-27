      expect_exact_semantic_replay(
        "duplicate",
        scenario.at("duplicateRequestId").get<uint64_t>(),
        "register_subscription",
        identity,
        true);
      const auto capacity_identity = Identity(
        {identity.key.contract_id + 1, 1});
      expect_exact_semantic_replay(
        "capacity",
        scenario.at("capacityRequestId").get<uint64_t>(),
        "register_subscription",
        capacity_identity,
        true);
      const auto unknown_identity = Identity(
        {identity.key.contract_id + 2, 1});
      expect_exact_semantic_replay(
        "unknown",
        scenario.at("unknownRequestId").get<uint64_t>(),
        "unregister_subscription",
        unknown_identity,
        false);
      EXPECT_EQ(
        scenario.at("expectedOutstandingRequests").get<uint64_t>(),
        replay.outstanding_requests());
      EXPECT_EQ(1U, contracts.contract_count());
    } else if (id == "contract_claim_blocks_external_cancel") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      u2r2::ContractAuthority contracts(
        limits,
        [&](u2r2::Operation operation,
          uint64_t request_id,
          const u2r2::ProtocolError & error)
        {
          EXPECT_EQ(
            scenario.at("responseOperation").get<std::string>(),
            OperationToken(operation));
          EXPECT_EQ(
            scenario.at("abortErrorCode").get<std::string>(),
            error.code());
          return u2r2::OutboundFrame::control(
            OperationToken(operation) + ":" + std::to_string(request_id),
            Bytes(scenario.at("abortResponseHex").get<std::string>()));
        });
      const auto identity = Identity(Key(scenario));
      const auto request = RequestBytes("register_subscription", identity);
      const auto request_id = scenario.at("requestId").get<uint64_t>();
      auto response = replay.admit(request_id, request, 1, scheduler);
      auto registration = contracts.begin_registration(
        identity,
        scheduler,
        replay,
        response);
      EXPECT_THROW(replay.cancel_pending(response), std::logic_error);
      EXPECT_THROW(
        replay.abort(
          response,
          Bytes(scenario.at("abortResponseHex").get<std::string>())),
        std::logic_error);
      contracts.abort_registration(
        registration,
        scheduler,
        replay,
        response,
        u2r2::ProtocolError(
          scenario.at("abortErrorCode").get<std::string>(),
          "registration backend rejected the contract",
          false));
      EXPECT_EQ(
        Bytes(scenario.at("abortResponseHex").get<std::string>()),
        DrainOne(scheduler).bytes());
      EXPECT_EQ(
        scenario.at("expectedOutstandingRequests").get<uint64_t>(),
        replay.outstanding_requests());
      EXPECT_EQ(0U, contracts.contract_count());
      auto repeated = replay.admit(request_id, request, 1, scheduler);
      EXPECT_EQ(u2r2::ReplayDecision::replay_cached, repeated.decision());
      EXPECT_EQ(
        Bytes(scenario.at("abortResponseHex").get<std::string>()),
        DrainOne(scheduler).bytes());
    } else if (id == "removal_abort_restores_ready_contract") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      u2r2::ContractAuthority contracts(
        limits,
        [&](u2r2::Operation operation,
          uint64_t request_id,
          const u2r2::ProtocolError & error)
        {
          EXPECT_EQ(
            scenario.at("responseOperation").get<std::string>(),
            OperationToken(operation));
          EXPECT_EQ(
            scenario.at("abortErrorCode").get<std::string>(),
            error.code());
          return u2r2::OutboundFrame::control(
            OperationToken(operation) + ":" + std::to_string(request_id),
            Bytes(scenario.at("abortResponseHex").get<std::string>()));
        });
      const auto key = Key(scenario);
      const auto identity = Identity(key);
      auto ready_response = replay.admit(
        scenario.at("registerRequestId").get<uint64_t>(),
        RequestBytes("register_subscription", identity),
        1,
        scheduler);
      auto registration = contracts.begin_registration(
        identity, scheduler, replay, ready_response);
      contracts.commit_ready(
        registration,
        replay,
        ready_response,
        u2r2::OutboundFrame::control("subscription_ready", Bytes("01")));
      (void)DrainOne(scheduler);
      auto removed_response = replay.admit(
        scenario.at("unregisterRequestId").get<uint64_t>(),
        RequestBytes("unregister_subscription", identity),
        1,
        scheduler);
      auto removal = contracts.begin_unregister(
        identity, scheduler, replay, removed_response);
      contracts.abort_removal(
        removal,
        scheduler,
        replay,
        removed_response,
        u2r2::ProtocolError(
          scenario.at("abortErrorCode").get<std::string>(),
          "unregister backend timed out",
          false));
      EXPECT_EQ(
        Bytes(scenario.at("abortResponseHex").get<std::string>()),
        DrainOne(scheduler).bytes());
      EXPECT_EQ(
        scenario.at("expectedContractCount").get<uint64_t>(),
        contracts.contract_count());
      EXPECT_EQ(
        u2r2::MessageAdmission::accepted,
        contracts.admit_message(
          identity, scenario.at("messageSequence").get<uint64_t>()));
      EXPECT_EQ(
        u2r2::EnqueueDisposition::accepted,
        scheduler.enqueue_data(
          u2r2::OutboundFrame::data("message", key, 1, Bytes("aa")),
          u2r2::QueueOverflowPolicy::reject));
    } else if (id == "cached_replay_rejects_wrong_scheduler") {
      u2r2::BoundedOutboundScheduler original(limits);
      u2r2::BoundedOutboundScheduler wrong(limits);
      u2r2::RequestReplayAuthority replay(limits);
      const auto request =
        Bytes(scenario.at("canonicalRequestHex").get<std::string>());
      const auto response =
        Bytes(scenario.at("responseHex").get<std::string>());
      const auto request_id = scenario.at("requestId").get<uint64_t>();
      auto first = replay.admit(
        request_id,
        request,
        static_cast<uint64_t>(response.size()),
        original);
      replay.complete(first, response);
      (void)DrainOne(original);
      EXPECT_THROW(
        (void)replay.admit(
          request_id,
          request,
          static_cast<uint64_t>(response.size()),
          wrong),
        std::logic_error);
      EXPECT_EQ(
        scenario.at("expectedWrongSchedulerDepth").get<uint64_t>(),
        wrong.total_queued_depth());
      auto repeated = replay.admit(
        request_id,
        request,
        static_cast<uint64_t>(response.size()),
        original);
      EXPECT_EQ(u2r2::ReplayDecision::replay_cached, repeated.decision());
      EXPECT_EQ(response, DrainOne(original).bytes());
    } else if (id == "invalid_overflow_policy_has_no_side_effects") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      ExpectProtocolError(scenario, [&]() {
        (void)scheduler.enqueue_data(
          u2r2::OutboundFrame::data(
            "invalid", {1, 1}, 1, Bytes("01")),
          static_cast<u2r2::QueueOverflowPolicy>(
            scenario.at("invalidPolicy").get<int>()));
      });
      EXPECT_EQ(
        scenario.at("expectedQueuedDepth").get<uint64_t>(),
        scheduler.total_queued_depth());
      EXPECT_EQ(
        scenario.at("expectedQueuedBytes").get<uint64_t>(),
        scheduler.queued_bytes());
    } else if (id == "replay_responses_respect_control_fairness") {
      auto bounded = limits.with({{"controlBurstLimit", 2}});
      u2r2::BoundedOutboundScheduler scheduler(bounded);
      u2r2::RequestReplayAuthority replay(bounded);
      EXPECT_EQ(
        u2r2::EnqueueDisposition::accepted,
        scheduler.enqueue_data(
          u2r2::OutboundFrame::data("data", {1, 1}, 1, Bytes("01")),
          u2r2::QueueOverflowPolicy::reject));
      for (const auto & id_value : scenario.at("requestIds")) {
        const auto request_id = id_value.get<uint64_t>();
        auto response = replay.admit(
          request_id,
          {static_cast<uint8_t>(request_id)},
          1,
          scheduler);
        replay.complete(
          response,
          {static_cast<uint8_t>(request_id + 1)});
      }
      EXPECT_EQ(
        scenario.at("expectedOrder").get<std::vector<std::string>>(),
        DrainAll(scheduler));
    } else if (id == "response_transaction_rejects_wrong_scheduler") {
      u2r2::BoundedOutboundScheduler original(limits);
      u2r2::BoundedOutboundScheduler wrong(limits);
      u2r2::RequestReplayAuthority replay(limits);
      u2r2::ContractAuthority contracts(limits, DefaultSemanticErrorFrame);
      const auto identity = Identity(Key(scenario));
      auto response = replay.admit(
        scenario.at("requestId").get<uint64_t>(),
        RequestBytes("register_subscription", identity),
        1,
        original);
      EXPECT_THROW(
        contracts.begin_registration(
          identity, wrong, replay, response),
        std::logic_error);
      replay.cancel_pending(response);
      EXPECT_EQ(
        scenario.at("expectedOutstandingRequests").get<uint64_t>(),
        replay.outstanding_requests());
      EXPECT_EQ(
        scenario.at("expectedReservedDepth").get<uint64_t>(),
        original.total_queued_depth());
    } else if (id == "codec_consumes_session_limit_snapshot") {
      const auto bounded = limits.with({
        {"maxHeaderBytes", scenario.at("maxHeaderBytes").get<uint64_t>()},
        {"maxPayloadBytes", scenario.at("maxPayloadBytes").get<uint64_t>()},
        {"maxJsonDepth", scenario.at("maxJsonDepth").get<uint64_t>()}});
      const auto simple = nlohmann::json{{"ok", 1}};
      EXPECT_NO_THROW((void)u2r2::encode_frame(simple, Bytes("01"), bounded));
      auto wire_error = scenario;
      wire_error["expectedErrorCode"] =
        scenario.at("expectedWireErrorCode");
      ExpectProtocolError(wire_error, [&]() {
        (void)u2r2::encode_frame(
          nlohmann::json{{"padding", std::string(100, 'x')}},
          {},
          bounded);
      });
      ExpectProtocolError(wire_error, [&]() {
        (void)u2r2::encode_frame(simple, Bytes("0102"), bounded);
      });
      ExpectProtocolError(wire_error, [&]() {
        (void)u2r2::encode_frame(
          nlohmann::json{{"one", {{"two", {{"three", 1}}}}}},
          {},
          bounded);
      });
      auto configuration_error = scenario;
      configuration_error["expectedErrorCode"] =
        scenario.at("expectedConfigurationErrorCode");
      const auto invalid_fixed = limits.with({{"fixedFrameBytes", 1}});
      ExpectProtocolError(configuration_error, [&]() {
        (void)u2r2::encode_frame(simple, {}, invalid_fixed);
      });
    } else if (id == "pure_peer_close_transition") {
      u2r2::PureSessionLifecycle lifecycle;
      ExpectProtocolError(scenario, [&]() {lifecycle.peer_closed();});
      EXPECT_EQ(u2r2::PureSessionState::closed, lifecycle.state());
    } else if (id == "pure_timeout_transitions") {
      const std::unordered_map<std::string, u2r2::TimeoutKind> kinds{
        {"handshake", u2r2::TimeoutKind::handshake},
        {"partial_frame", u2r2::TimeoutKind::partial_frame},
        {"read", u2r2::TimeoutKind::read},
        {"write", u2r2::TimeoutKind::write},
        {"join", u2r2::TimeoutKind::join},
        {"shutdown", u2r2::TimeoutKind::shutdown}};
      for (const auto & kind : scenario.at("timeoutKinds")) {
        u2r2::PureSessionLifecycle lifecycle(limits);
        const auto parsed_kind = kinds.at(kind.get<std::string>());
        ExpectProtocolError(scenario, [&]() {
          lifecycle.timeout(
            parsed_kind, lifecycle.limit_for(parsed_kind));
        });
        EXPECT_EQ(u2r2::PureSessionState::closed, lifecycle.state());
      }
    } else {
      FAIL() << "unconsumed Commit2 fixture scenario: " << id;
    }
  }
  EXPECT_EQ(scenarios.size(), consumed.size());
}
}  // namespace
