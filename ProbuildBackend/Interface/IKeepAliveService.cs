namespace ProbuildBackend.Interface
{
    public interface IKeepAliveService
    {
        void StartPinging();
        void StopPinging();
    }
}
