namespace QuackDuck;

internal sealed class EnergyMeter
{
    private readonly int max;
    private int current;

    internal EnergyMeter(int max = 1000)
    {
        this.max = max;
        current = max;
    }

    internal int Current => current;
    internal int Max => max;
    internal double Percent => (double)current / max * 100d;
    internal bool IsDepleted => current <= 0;

    internal void Spend(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        current = System.Math.Max(0, current - amount);
    }

    internal void Restore(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        current = System.Math.Min(max, current + amount);
    }
}
