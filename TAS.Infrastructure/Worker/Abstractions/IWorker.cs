namespace TAS.Infrastructure.Worker.Abstractions
{
    public interface IWorker : IDisposable
    {
        void Start();
        void Stop();
        void Delay(TimeSpan time);
        void Until(TimeSpan time);
    }
}
