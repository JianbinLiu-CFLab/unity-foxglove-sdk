using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;
using Xunit;
namespace Unity.FoxgloveSDK.UnitTests.Harness
{
 public sealed class McapReplayBehaviorTests
 {
 [Fact] public void BoundedHistoryKeepsLatestMessagesWhenRecordsArriveOutOfOrder()
  {
   var engineType=typeof(McapReplayEngine); var candidateType=engineType.GetNestedType("HistoryCandidate",BindingFlags.NonPublic);
   var ctor=candidateType.GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(int),typeof(int),typeof(int),typeof(ushort),typeof(uint),typeof(ulong),typeof(ulong),typeof(ulong),typeof(ulong)},null);
   var insert=engineType.GetMethod("InsertBoundedHistoryCandidate",BindingFlags.Static|BindingFlags.NonPublic);
   var bounded=(System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(candidateType));
   foreach(var time in new ulong[]{50,10,20,30}) insert.Invoke(null,new[]{bounded,ctor.Invoke(new object[]{0,0,1,(ushort)1,(uint)0,time,time,0UL,0UL}),2});
   var times=bounded.Cast<object>().Select(x=>(ulong)candidateType.GetProperty("LogTime",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(x)).ToArray();
   Assert.Equal(new ulong[]{30,50},times);
  }
  [Fact] public void HistoryKeepsLatestMessagesAcrossOverlappingChunks()
  {
   var path=Path.Combine(Path.GetTempPath(),"history-overlap-"+Guid.NewGuid().ToString("N")+".mcap");
   try
   {
    using(var stream=File.Create(path)) using(var recorder=new McapRecorder(stream,null,new McapWriterOptions{UseChunking=true,ChunkSizeBytes=64,IndexTypes=McapIndexTypes.Chunk,UseStatistics=true},leaveOpen:true))
    { recorder.AddChannel(1,"/history","json","schema","jsonschema","{}"); recorder.WriteMessage(1,50,new byte[20]); recorder.WriteMessage(1,10,new byte[20]); recorder.WriteMessage(1,20,new byte[20]); recorder.WriteMessage(1,30,new byte[20]); recorder.Close(); }
    using var engine=new McapReplayEngine(); engine.Load(path); var result=engine.History(0,100,new List<McapMessage>(),2,null);
    Assert.Equal(new ulong[]{30,50},result.Select(x=>x.LogTime).ToArray());
   }
   finally { if(File.Exists(path)) File.Delete(path); }
  }
  [Fact] public void ReplayHistoryDrainAdvancesOffsetAndCompletesAfterFanout()
  {
   var b=new ReplayPanelHistoryBuffer(); b.Buffer.Add(new McapMessage{ChannelId=1,LogTime=10,Data=new byte[]{1}}); b.Buffer.Add(new McapMessage{ChannelId=1,LogTime=20,Data=new byte[]{2}}); b.BeginDrain(20);
   using var s=new FoxgloveSession("module4-history",new NullTransport()); s.RegisterChannel(new AdvertiseChannel{Id=(uint)McapReplayEngine.ReplayChannelIdBase|1u,Topic="/module4/history",Encoding="json"});
   b.DrainLocked(s,new Dictionary<ushort,string>{{1,"/module4/history"}},new ConsoleLogger(),1,0,0); Assert.True(b.DebugActive); Assert.Equal(2,b.DebugBufferedCount);
   b.DrainLocked(s,new Dictionary<ushort,string>{{1,"/module4/history"}},new ConsoleLogger(),2,0,0); Assert.False(b.DebugActive); Assert.Equal(0,b.DebugBufferedCount);
  }
  [Fact] public void McapFiltersMatchChannelOrTopicAndReturnBothMessages()
  { using var stream=BuildTwoChannelMcap(); using var loader=new McapDataLoader(stream,leaveOpen:true); var m=loader.CreateIterator(new McapDataLoaderQuery{ChannelIds=new List<ushort>{1},Topics=new List<string>{"/module4/b"}}).ToList(); Assert.Equal(new ushort[]{1,2},m.Select(x=>x.ChannelId).ToArray()); }
  [Fact] public void AmendmentAttachmentCountOverflowIsRejected()
  { var path=Path.Combine(Path.GetTempPath(),"module4-overflow-"+Guid.NewGuid().ToString("N")+".mcap"); try { using(var st=File.Create(path)) using(var w=new McapWriter(st,leaveOpen:true)){w.WriteMagic();w.WriteHeader("","module4");w.WriteDataEnd();var sum=new McapFileSummary{Statistics=new McapStatistics{AttachmentCount=uint.MaxValue}};McapSummarySerializer.WriteSummaryAndFooter(w,sum,true,true);w.WriteMagic();w.Flush();} using var a=new McapAmendmentWriter(path); a.AddAttachment("overflow","application/octet-stream",new byte[]{1},1); Assert.Throws<OverflowException>(()=>a.Close()); } finally {if(File.Exists(path))File.Delete(path); foreach(var x in Directory.GetFiles(Path.GetDirectoryName(path),Path.GetFileName(path)+".*.bak"))File.Delete(x);} }
  static MemoryStream BuildTwoChannelMcap(){var s=new MemoryStream();using(var w=new McapWriter(s,leaveOpen:true)){w.WriteMagic();w.WriteHeader("","module4");w.WriteChannel(1,0,"/module4/a","json",new Dictionary<string,string>());w.WriteChannel(2,0,"/module4/b","json",new Dictionary<string,string>());w.WriteMessage(1,1,10,10,Encoding.UTF8.GetBytes("a"));w.WriteMessage(2,1,20,20,Encoding.UTF8.GetBytes("b"));w.WriteDataEnd();var sum=new McapFileSummary{Statistics=new McapStatistics{MessageCount=2,ChannelCount=2,MessageStartTime=10,MessageEndTime=20}};sum.Channels.Add(new McapChannel{Id=1,Topic="/module4/a",MessageEncoding="json"});sum.Channels.Add(new McapChannel{Id=2,Topic="/module4/b",MessageEncoding="json"});McapSummarySerializer.WriteSummaryAndFooter(w,sum,true,true);w.WriteMagic();w.Flush();}s.Position=0;return s;}
  sealed class NullTransport:IFoxgloveTransport{public bool IsRunning=>false;public event Action<uint> OnClientConnected;public event Action<uint> OnClientDisconnected;public event Action<uint,string> OnTextReceived;public event Action<uint,byte[]> OnBinaryReceived;public void Start(string h,int p){}public void Stop(){}public void BroadcastText(string j){}public void BroadcastBinary(byte[] d){}public void SendText(uint c,string j){}public void SendBinary(uint c,byte[]d){}public void Dispose(){} }
 }
}
