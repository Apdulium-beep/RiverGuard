using RiverGuard.Core;

namespace RiverGuard;

public static partial class ExportService
{
    private static string[] ResultHeaders(SimulationResult r)=>r.HasMassBalance?Headers:new[]{"Створ","L, м","Zг, м","H, м","Hг, м","Qг, м³/с","V (53), м/с","Фронт, ч","Гребень, ч","Хвост, ч"};
    private static string ReportDiagnostics(SimulationResult r)
    {
        if(r.HasMassBalance)return Diagnostics(r);
        var d=r.Method1997??throw new InvalidOperationException("Отсутствует диагностика методики.");
        return $"{r.ModelVersion}. Баланс массы, сетка и временные ряды не вычисляются (не 0 %). Nв={F(d.ReservoirShape)}, K0=Kп={F(d.ValleyShape)}, Aп={F(d.BreachCoefficient)}, Wвр={F(d.AccidentVolumeMillion)} млн м³, Wс={F(d.ReleasedVolumeMillion)} млн м³; Hс={F(d.SourceFlowDepth)} м, Vс={F(d.SourceVelocity)} м/с, Qс={F(d.SourcePeakDischarge)} м³/с, Tс={F(d.EmptyingHours)} ч; H0={F(d.ZeroDepth)} м; итераций {d.Iterations}. Это экспериментальная транскрипция, а не аттестованная «Волна».";
    }
}
