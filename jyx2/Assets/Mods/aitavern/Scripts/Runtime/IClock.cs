namespace Jyx2.AITavern
{
    public interface IClock
    {
        long NowMs();   // monotonic milliseconds since some epoch
    }
}
