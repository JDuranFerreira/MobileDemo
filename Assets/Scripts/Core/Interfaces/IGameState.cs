namespace MobileDemo.Core.Interfaces
{
    public interface IGameState
    {
        void Enter();

        void Tick(float dt);

        void Exit();
    }
}
