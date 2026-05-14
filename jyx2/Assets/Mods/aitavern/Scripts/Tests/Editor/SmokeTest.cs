using NUnit.Framework;

namespace Jyx2.AITavern.Tests
{
    public class SmokeTest
    {
        [Test]
        public void OnePlusOne_Equals_Two()
        {
            Assert.AreEqual(2, 1 + 1);
        }

        [Test]
        public void FakeClock_Advances()
        {
            var clock = new FakeClock(0);
            Assert.AreEqual(0, clock.NowMs());
            clock.Advance(500);
            Assert.AreEqual(500, clock.NowMs());
        }

        [Test]
        public void MockGrokClient_ReturnsCannedResponse()
        {
            // UTF 1.1.31 doesn't auto-await Task from [Test]; block via .GetAwaiter().GetResult().
            // The MockGrokClient is synchronous internally (no real I/O), so .Result is safe here.
            var mock = new MockGrokClient();
            mock.Canned["黄蓉"] = "你为何在此？";
            var result = mock.CompleteChatAsync("你是黄蓉。", new System.Collections.Generic.List<(string, string)>(), 200).GetAwaiter().GetResult();
            Assert.AreEqual("你为何在此？", result);
        }
    }
}
