using System;
using Xunit;
using Unity.FoxgloveSDK.Components.Publishing;
using Unity.FoxgloveSDK.Components.Publishing.MessagePack;
using Unity.FoxgloveSDK.Components.Publishing.Session;
using Unity.FoxgloveSDK.Components;

namespace Unity.FoxgloveSDK.Tests.Components
{
    public class ComponentPublisherContractCatalogTests
    {
        private static ComponentPublisherSessionSnapshot Snapshot(params ComponentPublisherContractDraft[] drafts)
            => new ComponentPublisherSessionBuilder().Build(42, drafts);

        [Fact]
        public void NotReadyReturnsBoundedSessionNotReadyContract()
        {
            var response = ComponentPublisherContractCatalog.BuildResponse(null);
            Assert.False((bool)response["sessionReady"]);
            Assert.Equal("session_not_ready", response["results"][0]["code"].ToObject<string>());
        }

        [Fact]
        public void ActiveProjectionIsStableAndSorted()
        {
            var first = new object();
            var second = new object();
            var snapshot = Snapshot(
                new ComponentPublisherContractDraft(second, "b", typeof(string), "mode", "/z", "schema.z", PublisherEffectiveEncoding.MsgPack, PublisherEffectiveEncoding.MsgPack,
                    new ComponentMessagePackGeneratedEntry(typeof(string), "schema.z", "shape-z", true, true, "")),
                new ComponentPublisherContractDraft(first, "a", typeof(int), "mode", "/a", "schema.a", PublisherEffectiveEncoding.Protobuf, PublisherEffectiveEncoding.Protobuf));
            var a = ComponentPublisherContractCatalog.BuildResponse(snapshot);
            var b = ComponentPublisherContractCatalog.BuildResponse(Snapshot(
                new ComponentPublisherContractDraft(first, "a", typeof(int), "mode", "/a", "schema.a", PublisherEffectiveEncoding.Protobuf, PublisherEffectiveEncoding.Protobuf),
                new ComponentPublisherContractDraft(second, "b", typeof(string), "mode", "/z", "schema.z", PublisherEffectiveEncoding.MsgPack, PublisherEffectiveEncoding.MsgPack,
                    new ComponentMessagePackGeneratedEntry(typeof(string), "schema.z", "shape-z", true, true, ""))));
            Assert.Equal(a.ToString(), b.ToString());
            Assert.Equal("/a", a["results"][0]["topic"].ToObject<string>());
            Assert.Equal("schemaless", a["results"][1]["wireSchema"].ToObject<string>());
        }

        [Fact]
        public void UnsupportedAndOversizedDiagnosticsFailClosed()
        {
            var draft = new ComponentPublisherContractDraft(new object(), "id", typeof(object), "mode", "/x", "schema", PublisherEffectiveEncoding.MsgPack, PublisherEffectiveEncoding.MsgPack,
                new ComponentMessagePackGeneratedEntry(typeof(object), "schema", "shape", false, true, "codec unavailable"));
            var response = ComponentPublisherContractCatalog.BuildResponse(Snapshot(draft), includeTypeShape: true);
            Assert.False(response["results"][0]["available"].ToObject<bool>());
            Assert.Equal("codec unavailable", response["results"][0]["diagnostic"].ToObject<string>());
            Assert.Equal(Newtonsoft.Json.Linq.JTokenType.Null, response["results"][0]["typeShape"].Type);
        }
    }
}
