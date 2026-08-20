namespace EwonSqlInjector.Models;

public sealed class EwonRecord
{
    public long TimeInt { get; init; }
    public string TimeStr { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }

    public decimal Turbidity { get; init; }
    public decimal FreeChlorine { get; init; }
    public decimal PH { get; init; }
    public decimal LeftFlowRate { get; init; }
    public decimal LeftTotal { get; init; }
    public decimal RightFlowRate { get; init; }
    public decimal RightTotal { get; init; }
    public decimal PressureA { get; init; }
    public decimal PressureB { get; init; }
}
