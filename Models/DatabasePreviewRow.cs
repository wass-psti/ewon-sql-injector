namespace EwonSqlInjector.Models;

public sealed class DatabasePreviewRow
{
    public long? ID { get; init; }

    public decimal rec_Turbidity_NTU { get; init; }
    public decimal rec_FreeChlorine_ppm { get; init; }
    public decimal rec_AcidBase_pH { get; init; }
    public decimal rec_FlwMtr_A_Flowrate_m3p { get; init; }
    public decimal rec_FlwMtr_A_Tot_m3 { get; init; }
    public decimal rec_FlwMtr_B_Flowrate_m3p { get; init; }
    public decimal rec_FlwMtr_B_Tot_m3 { get; init; }
    public decimal rec_Pressure_A { get; init; }
    public decimal rec_Pressure_B { get; init; }

    public string rec_DATE { get; init; } = string.Empty;
    public DateTime rec_TS { get; init; }
}
