namespace RiverGuard.Core;

// Поддерживаемые механизмы аварии
public static class ScenarioMode
{
    public const string Breach="Прорыв плотины";
    public const string Overflow="Перелив через гребень";
    public const string Gate="Аварийное открытие затвора";
    public const string Circular="Круглое отверстие в теле плотины";
    public static bool IsOverflow(string? value)=>string.Equals(value,Overflow,StringComparison.OrdinalIgnoreCase);
    public static bool IsGate(string? value)=>string.Equals(value,Gate,StringComparison.OrdinalIgnoreCase);
    public static bool IsCircular(string? value)=>string.Equals(value,Circular,StringComparison.OrdinalIgnoreCase);
    public static bool HasFormation(string? value)=>IsGate(value)||string.Equals(value,Breach,StringComparison.OrdinalIgnoreCase);
    public static bool IsKnown(string? value)=>IsOverflow(value)||IsGate(value)||IsCircular(value)||string.Equals(value,Breach,StringComparison.OrdinalIgnoreCase);
    public static string FlowTitle(string? value)=>IsGate(value)?"Гидрограф через затвор":IsCircular(value)?"Гидрограф через отверстие":IsOverflow(value)?"Гидрограф перелива":"Гидрограф прорана";
}
