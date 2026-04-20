namespace BuildigBackend.Interface
{
    public interface IKeepAliveService
    {
        void StartPinging();
        void StopPinging();
    }
}

