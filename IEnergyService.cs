namespace QuackDuck;

internal interface IEnergyService
{
    int Current { get; }
    int Max { get; }
    double Percent { get; }
    bool IsDepleted { get; }

    void Spend(int amount);
    void Restore(int amount);
    void Set(int amount);
}
