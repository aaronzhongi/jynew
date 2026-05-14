using UnityEngine;

namespace Jyx2.AITavern
{
    public class SystemClock : IClock
    {
        public long NowMs() => (long)(Time.unscaledTimeAsDouble * 1000.0);
    }
}
