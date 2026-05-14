namespace Jyx2.AITavern.Tests
{
    public class FakeClock : IClock
    {
        public long Current { get; private set; }

        public FakeClock(long start = 0)
        {
            Current = start;
        }

        public long NowMs() => Current;

        public void Advance(long ms)
        {
            Current += ms;
        }

        public void SetTo(long ms)
        {
            Current = ms;
        }
    }
}
