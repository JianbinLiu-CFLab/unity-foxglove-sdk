// Phase 141D negative manual demo snippet.
//
// Keep this file commented out by default. For a demo video or analyzer
// acceptance pass, uncomment it to show Unity reporting DTO diagnostics for
// unsupported service request shapes.
//
// Expected analyzer behavior after uncommenting:
// - IEnumerable<T> request member should produce FOXSERVICE003.
// - Get-only scalar and readonly fields should produce FOXSERVICE007 warnings.
//
// This file is intentionally a negative sample; comment it out again after the
// demo so normal scene compilation is not blocked by the expected analyzer
// error.
//using System.Collections.Generic;
//using UnityEngine;
//using Unity.FoxgloveSDK.Components;

//public partial class FoxService141DBadDtoSmoke : MonoBehaviour
//{
//    public sealed class BadRequest
//    {
//        public IEnumerable<int> sequence { get; set; }
//        public string getOnlyScalar => "bad";
//        public readonly int readonlyValue;
//    }

//    public sealed class Response
//    {
//        public string status { get; set; }
//    }

//    [FoxService("/phase141d/bad_dto")]
//    private Response Bad(BadRequest request) => new Response { status = "bad" };
//}
