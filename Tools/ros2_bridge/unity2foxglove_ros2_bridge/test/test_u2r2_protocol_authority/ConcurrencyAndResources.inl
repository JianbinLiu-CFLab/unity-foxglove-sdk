      const auto replay_limits = limits.with({
        {"maxOutstandingRequests", 2}, {"reservedControlQueueDepth", 3}});
      u2r2::BoundedOutboundScheduler replay_scheduler(replay_limits);
      u2r2::RequestReplayAuthority replay(replay_limits);
      std::vector<u2r2::ReplayAdmission> pending;
      pending.push_back(replay.admit(1, Bytes("01"), 1, replay_scheduler));
      pending.push_back(replay.admit(2, Bytes("02"), 1, replay_scheduler));
      ExpectProtocolError(scenario, [&]() {
        (void)replay.admit(3, Bytes("03"), 1, replay_scheduler);
      });
    } else if (id == "limits_diagnostic_snapshot_is_immutable") {
      const auto snapshot = limits.to_diagnostic_snapshot();
      EXPECT_EQ(scenario.at("expectedLimitCount").get<size_t>(), snapshot.size());
      EXPECT_EQ(
        snapshot,
        u2r2::ProtocolLimits::defaults().to_diagnostic_snapshot());
      auto source = snapshot;
      const auto independent = u2r2::ProtocolLimits::from_diagnostic_snapshot(source);
      source["maxConnections"] = 999;
      EXPECT_EQ(limits.max_connections(), independent.max_connections());
    } else if (id == "limits_configuration_fails_closed") {
      for (const auto & mutation_value : scenario.at("invalidMutations")) {
        const auto mutation = mutation_value.get<std::string>();
        auto values = limits.to_diagnostic_snapshot();
        if (mutation == "missing_field") {
          values.erase("maxConnections");
          ExpectProtocolError(scenario, [&]() {
            (void)u2r2::ProtocolLimits::from_diagnostic_snapshot(values);
          });
        } else if (mutation == "unknown_field") {
          values["unknownLimit"] = 1;
          ExpectProtocolError(scenario, [&]() {
            (void)u2r2::ProtocolLimits::from_diagnostic_snapshot(values);
          });
        } else if (mutation == "zero_value") {
          values["readTimeoutMs"] = 0;
          ExpectProtocolError(scenario, [&]() {
            (void)u2r2::ProtocolLimits::from_diagnostic_snapshot(values);
          });
        } else if (mutation == "data_sessions_not_one") {
          values["maxDataSessions"] = 2;
          values["maxConnections"] =
            values["maxDataSessions"] + values["maxProbes"];
          ExpectProtocolError(scenario, [&]() {
            (void)u2r2::ProtocolLimits::from_diagnostic_snapshot(values);
          });
        } else if (mutation == "connections_below_roles") {
          values["maxConnections"] =
            values["maxDataSessions"] + values["maxProbes"] - 1;
          ExpectProtocolError(scenario, [&]() {
            (void)u2r2::ProtocolLimits::from_diagnostic_snapshot(values);
          });
        } else if (mutation == "per_contract_below_max_frame") {
          values["maxPerContractQueueBytes"] =
            values["fixedFrameBytes"] + values["maxHeaderBytes"] +
            values["maxPayloadBytes"] - 1;
          ExpectProtocolError(scenario, [&]() {
            (void)u2r2::ProtocolLimits::from_diagnostic_snapshot(values);
          });
        } else if (mutation == "queued_below_max_frame") {
          values["maxQueuedBytes"] =
            values["fixedFrameBytes"] + values["maxHeaderBytes"] +
            values["maxPayloadBytes"] - 1;
          ExpectProtocolError(scenario, [&]() {
            (void)u2r2::ProtocolLimits::from_diagnostic_snapshot(values);
          });
        } else if (mutation == "control_depth_exceeds_total") {
          values["reservedControlQueueDepth"] = values["maxTotalQueueDepth"] + 1;
          ExpectProtocolError(scenario, [&]() {
            (void)u2r2::ProtocolLimits::from_diagnostic_snapshot(values);
          });
        } else if (mutation == "control_burst_exceeds_depth") {
          values["controlBurstLimit"] = values["reservedControlQueueDepth"] + 1;
          ExpectProtocolError(scenario, [&]() {
            (void)u2r2::ProtocolLimits::from_diagnostic_snapshot(values);
          });
        } else if (mutation == "revoked_bound_overflow") {
          values["maxContracts"] = std::numeric_limits<uint64_t>::max();
          values["maxTombstones"] = 1;
          ExpectProtocolError(scenario, [&]() {
            (void)u2r2::ProtocolLimits::from_diagnostic_snapshot(values);
          });
        } else if (mutation == "header_above_uint32") {
          values["maxHeaderBytes"] =
            static_cast<uint64_t>(std::numeric_limits<uint32_t>::max()) + 1;
          ExpectProtocolError(scenario, [&]() {
            (void)u2r2::ProtocolLimits::from_diagnostic_snapshot(values);
          });
        } else if (mutation == "payload_above_uint32") {
          values["maxPayloadBytes"] =
            static_cast<uint64_t>(std::numeric_limits<uint32_t>::max()) + 1;
          ExpectProtocolError(scenario, [&]() {
            (void)u2r2::ProtocolLimits::from_diagnostic_snapshot(values);
          });
        } else if (mutation == "json_depth_above_protocol_max") {
          values["maxJsonDepth"] = 65;
          ExpectProtocolError(scenario, [&]() {
            (void)u2r2::ProtocolLimits::from_diagnostic_snapshot(values);
          });
        } else if (mutation == "unknown_with_field") {
          ExpectProtocolError(scenario, [&]() {
            (void)limits.with({{"unknownLimit", 1}});
          });
        } else {
          FAIL() << "unknown limits mutation: " << mutation;
        }
      }
    } else if (id == "ready_unregister_full_ordering") {
      const auto key = Key(scenario);
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::ContractAuthority contracts(limits, DefaultSemanticErrorFrame);
      u2r2::RequestReplayAuthority replay(limits);
      const auto identity = Identity(key);
      auto ready_response = replay.admit(
        1, RequestBytes("register_subscription", identity), 1, scheduler);
      auto registration = contracts.begin_registration(
        identity, scheduler, replay, ready_response);
      auto pre_ready = scenario;
      pre_ready["expectedErrorCode"] =
        scenario.at("expectedPreReadyErrorCode");
      ExpectProtocolError(pre_ready, [&]() {
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
        contracts.admit_message(identity, 1));
      std::vector<std::string> order{DrainOne(scheduler).token()};
      (void)scheduler.enqueue_data(
        u2r2::OutboundFrame::data("queued", key, 2, Bytes("01")),
        u2r2::QueueOverflowPolicy::reject);
      (void)scheduler.enqueue_data(
        u2r2::OutboundFrame::data("writer", key, 3, Bytes("02")),
        u2r2::QueueOverflowPolicy::reject);
      auto writer = scheduler.try_begin_write();
      ASSERT_TRUE(writer.has_value());
      order.push_back(writer->frame().token());
      auto removed_response = replay.admit(
        2, RequestBytes("unregister_subscription", identity), 1, scheduler);
      auto removal = contracts.begin_unregister(
        identity, scheduler, replay, removed_response);
      EXPECT_EQ(0U, scheduler.data_queued_depth());
      EXPECT_FALSE(contracts.try_commit_removed(
          removal,
          scheduler,
          replay,
          removed_response,
          u2r2::OutboundFrame::control("subscription_removed", Bytes("03"))));
      writer.reset();
      EXPECT_TRUE(contracts.try_commit_removed(
          removal,
          scheduler,
          replay,
          removed_response,
          u2r2::OutboundFrame::control("subscription_removed", Bytes("03"))));
      order.push_back(DrainOne(scheduler).token());
      EXPECT_EQ(scenario.at("expectedOrder").get<std::vector<std::string>>(), order);
      EXPECT_EQ(
        u2r2::MessageAdmission::late_tombstone,
        contracts.admit_message(identity, 2));
    } else if (id == "contract_identity_validation") {
      for (const auto & topic : scenario.at("validTopics")) {
        const auto parsed = ParseRegistration(
          topic.get<std::string>(), "sensor_msgs/msg/Image",
          scenario.at("validQos").at(0));
        EXPECT_EQ(topic.get<std::string>(), parsed.topic);
        EXPECT_EQ("sensor_msgs/msg/Image", parsed.schema_name);
      }
      for (const auto & topic : scenario.at("invalidTopics")) {
        ExpectProtocolError(scenario, [&]() {
          (void)ParseRegistration(
            topic.get<std::string>(), "sensor_msgs/msg/Image",
            scenario.at("validQos").at(0));
        });
      }
      for (const auto & type : scenario.at("validTypes")) {
        const auto parsed = ParseRegistration(
          "/camera/front", type.get<std::string>(),
          scenario.at("validQos").at(0));
        EXPECT_EQ(type.get<std::string>(), parsed.schema_name);
      }
      for (const auto & type : scenario.at("invalidTypes")) {
        ExpectProtocolError(scenario, [&]() {
          (void)ParseRegistration(
            "/camera/front", type.get<std::string>(),
            scenario.at("validQos").at(0));
        });
      }
      for (const auto & qos : scenario.at("validQos")) {
        const auto parsed = ParseRegistration(
          "/camera/front", "sensor_msgs/msg/Image", qos);
        ASSERT_TRUE(parsed.qos.has_value());
        EXPECT_EQ(qos.at("profile").get<std::string>(), parsed.qos->profile);
        EXPECT_EQ(
          qos.at("reliability").get<std::string>(),
          parsed.qos->reliability);
        EXPECT_EQ(
          qos.at("durability").get<std::string>(),
          parsed.qos->durability);
        EXPECT_EQ(qos.at("history").get<std::string>(), parsed.qos->history);
        EXPECT_EQ(qos.at("depth").get<uint32_t>(), parsed.qos->depth);
      }
      for (const auto & qos : scenario.at("invalidQos")) {
        ExpectProtocolError(scenario, [&]() {
          (void)ParseRegistration(
            "/camera/front", "sensor_msgs/msg/Image", qos);
        });
      }
      for (const auto & mutation : scenario.at("invalidQosShapes")) {
        const auto invalid = InvalidQosShape(
          scenario.at("validQos").at(0),
          mutation.get<std::string>());
        ExpectProtocolError(scenario, [&]() {
          (void)ParseRegistration(
            "/camera/front", "sensor_msgs/msg/Image", invalid);
        });
      }
      const auto & boundaries = scenario.at("typeLengthBoundaries");
      const auto valid_package = std::string(
        boundaries.at("validPackageLength").get<size_t>(), 'a');
      const auto invalid_package = std::string(
        boundaries.at("invalidPackageLength").get<size_t>(), 'a');
      const auto valid_type =
        "T" + std::string(
        boundaries.at("validTypeLength").get<size_t>() - 1, 'a');
      const auto invalid_type =
        "T" + std::string(
        boundaries.at("invalidTypeLength").get<size_t>() - 1, 'a');
      (void)ParseRegistration(
        "/camera/front",
        valid_package + "/msg/" + valid_type,
        scenario.at("validQos").at(0));
      ExpectProtocolError(scenario, [&]() {
        (void)ParseRegistration(
          "/camera/front",
          invalid_package + "/msg/Image",
          scenario.at("validQos").at(0));
      });
      ExpectProtocolError(scenario, [&]() {
        (void)ParseRegistration(
          "/camera/front",
          "sensor_msgs/msg/" + invalid_type,
          scenario.at("validQos").at(0));
      });
      for (const auto & invalid : scenario.at("invalidDirections")) {
        ExpectProtocolError(scenario, [&]() {
          (void)u2r2::ContractIdentity(
            {41, 7},
            static_cast<u2r2::ContractDirection>(
              invalid.get<uint32_t>()),
            "/camera/front",
            "sensor_msgs/msg/Image",
            {"default", "reliable", "volatile", "keep_last", 10});
        });
      }
    } else if (id == "contract_identity_alias_and_replay") {
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
      auto ready_response = replay.admit(
        register_id, register_request, 1, scheduler);
      auto registration = contracts.begin_registration(
        identity, scheduler, replay, ready_response);
      contracts.commit_ready(
        registration,
        replay,
        ready_response,
        u2r2::OutboundFrame::control("subscription_ready", Bytes("01")));
      (void)DrainOne(scheduler);

      auto repeated = replay.admit(
        register_id, register_request, 1, scheduler);
      EXPECT_EQ(u2r2::ReplayDecision::replay_cached, repeated.decision());
      auto replayed_registration = contracts.begin_registration(
        identity, scheduler, replay, repeated);
      EXPECT_TRUE(replayed_registration.replayed());
      contracts.commit_ready(
        replayed_registration,
        replay,
        repeated,
        u2r2::OutboundFrame::control("must-not-send", Bytes("01")));
      EXPECT_EQ(
        scenario.at("expectedResponseCount").get<uint64_t>(),
        scheduler.total_queued_depth());
      EXPECT_EQ("replay:21", DrainOne(scheduler).token());

      auto request_id = scenario.at("aliasStartRequestId").get<uint64_t>();
      for (const auto & mutation_value : scenario.at("aliasMutations")) {
        auto alias = identity;
        const auto mutation = mutation_value.get<std::string>();
        if (mutation == "topic") {
          alias.topic = "/camera/rear";
        } else if (mutation == "schemaName") {
          alias.schema_name = "demo_interfaces/msg/Telemetry";
        } else if (mutation == "profile") {
          alias.qos.profile = "sensor_data";
        } else if (mutation == "reliability") {
          alias.qos.reliability = "best_effort";
        } else if (mutation == "durability") {
          alias.qos.durability = "transient_local";
        } else if (mutation == "history") {
          alias.qos.history = "keep_all";
          alias.qos.depth = 0;
        } else if (mutation == "depth") {
          alias.qos.depth = 11;
        } else if (mutation == "direction") {
          alias.direction = u2r2::ContractDirection::publish;
        } else {
          FAIL() << mutation;
        }
        auto alias_response = replay.admit(
          request_id++,
          RequestBytes("register_subscription", alias),
          1,
          scheduler);
        ExpectProtocolError(scenario, [&]() {
          (void)contracts.begin_registration(
            alias, scheduler, replay, alias_response);
        });
        EXPECT_NE(
          std::string::npos,
          DrainOne(scheduler).token().find("subscription_ready"));
      }
      EXPECT_EQ(1U, contracts.contract_count());
    } else if (id == "fresh_registration_requires_subscribe_direction") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      u2r2::ContractAuthority contracts(limits, DefaultSemanticErrorFrame);
      const auto identity = Identity(
        Key(scenario),
        "/camera/front",
        "sensor_msgs/msg/Image",
        u2r2::ContractDirection::publish);
      const auto request = RequestBytes("register_subscription", identity);
      const auto request_id = scenario.at("requestId").get<uint64_t>();
      auto response = replay.admit(
        request_id,
        request,
        1,
        scheduler);
      ExpectProtocolError(scenario, [&]() {
        (void)contracts.begin_registration(
          identity,
          scheduler,
          replay,
          response);
      });
      const auto first = DrainOne(scheduler);
      EXPECT_EQ(
        scenario.at("expectedResponseToken").get<std::string>(),
        first.token());
      EXPECT_EQ(
        Bytes(scenario.at("expectedResponseHex").get<std::string>()),
        first.bytes());
      EXPECT_EQ(0U, contracts.contract_count());
      EXPECT_EQ(0U, replay.outstanding_requests());

      auto repeated = replay.admit(
        request_id,
        request,
        1,
        scheduler);
      EXPECT_EQ(u2r2::ReplayDecision::replay_cached, repeated.decision());
      EXPECT_EQ(first.bytes(), repeated.cached_response());
      EXPECT_EQ(first.bytes(), DrainOne(scheduler).bytes());
    } else if (id == "message_requires_frozen_contract_identity") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      u2r2::ContractAuthority contracts(limits, DefaultSemanticErrorFrame);
      const auto identity = Identity(Key(scenario));
      auto response = replay.admit(
        1,
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
      (void)DrainOne(scheduler);
