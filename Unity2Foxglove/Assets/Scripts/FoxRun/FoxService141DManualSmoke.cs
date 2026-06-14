// Phase 141D manual demo snippet.
//
// Keep this file commented out by default so the sample scene does not always
// advertise the temporary /phase141d/manual_dto service. For a demo video or
// manual acceptance pass, uncomment the file, let Unity recompile, then call
// /phase141d/manual_dto from Foxglove. Expected response:
//
//   status: "ok"
//   samples: 2 items
//   counts: 1 key
//
// This is the positive DTO-policy demo: HashSet<T>, ICollection<T>, get-only
// mutable List<T>, IReadOnlyCollection<T> response members, and
// SortedDictionary<string, T> should be accepted.
//using System.Collections.Generic;
//using UnityEngine;
//using Unity.FoxgloveSDK.Components;

//public partial class FoxService141DManualSmoke : MonoBehaviour
//{
//    public sealed class Request
//    {
//        public HashSet<string> tags { get; set; } = new();
//        public ICollection<float> values { get; set; } = new List<float>();
//        public List<string> notes { get; } = new();
//    }

//    public sealed class Response
//    {
//        public IReadOnlyCollection<double> samples { get; set; } = new[] { 1.0, 2.0 };
//        public SortedDictionary<string, int> counts { get; set; } = new();
//        public string status { get; set; } = "ok";
//    }

//    [FoxService("/phase141d/manual_dto")]
//    private Response CheckDto(Request request)
//    {
//        return new Response
//        {
//            counts = new SortedDictionary<string, int>
//            {
//                ["tags"] = request.tags?.Count ?? 0
//            }
//        };
//    }
//}
