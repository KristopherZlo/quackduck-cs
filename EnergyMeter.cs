namespace QuackDuck;

internal sealed class EnergyMeter : IEnergyService
{
    private readonly int max;
    private int current;

    internal EnergyMeter(int max = 1000)
    {
        this.max = max;
        current = max;
    }

    public int Current => current;
    public int Max => max;
    public double Percent => (double)current / max * 100d;
    public bool IsDepleted => current <= 0;

    public void Spend(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        current = System.Math.Max(0, current - amount);
    }

    public void Restore(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        current = System.Math.Min(max, current + amount);
    }

    public void Set(int amount)
    {
        current = System.Math.Clamp(amount, 0, max);
    }
}
