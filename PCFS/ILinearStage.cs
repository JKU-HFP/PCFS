namespace PIStage_Library
{
    public interface ILinearStage
    {
        bool ControllerReady { get; }

        void Connect(string filter);
        bool Move_Absolute(double position);
        bool Move_Relative(double position);
        bool SetVelocity(double velocity);
        bool WaitForPos();
    }
}