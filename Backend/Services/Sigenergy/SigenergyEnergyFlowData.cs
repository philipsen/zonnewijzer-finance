using System.Text.Json.Serialization;

namespace Backend.Services.Sigenergy;

/// <summary>Inner object after double-parsing the Sigen <c>data</c> string (energy flow / login snapshot).</summary>
public class SigenergyEnergyFlowData
{
    [JsonPropertyName("pvPower")]
    public double PvPower { get; set; }

    [JsonPropertyName("gridPower")]
    public double GridPower { get; set; }

    [JsonPropertyName("evPower")]
    public double EvPower { get; set; }

    [JsonPropertyName("acPower")]
    public double AcPower { get; set; }

    [JsonPropertyName("loadPower")]
    public double LoadPower { get; set; }

    [JsonPropertyName("heatPumpPower")]
    public double HeatPumpPower { get; set; }

    [JsonPropertyName("batteryPower")]
    public double BatteryPower { get; set; }

    [JsonPropertyName("batterySoc")]
    public double BatterySoc { get; set; }
}
