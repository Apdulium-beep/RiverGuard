namespace RiverGuard.Core;

public static class Method1997Examples
{
    public static ProjectData Control()
    {
        var p=Examples.Valley();p.Name="Методика 1997 · контроль формул (синтетический)";p.CalculationMode=CalculationModes.Method1997;
        p.Method1997.ReferenceDepth=10;p.Method1997.ReservoirWidth=400;p.Method1997.AreaFraction=.5;
        p.ReservoirLevel=110;p.DamBed=100;p.ReservoirVolume=10;p.ReservoirArea=2;p.ReservoirReferenceLevel=null;p.LowerBedElevation=null;
        p.SillElevation=101;p.FormationMinutes=0;p.LowerDepth=2;p.LowerWidth=80;p.LowerVelocity=.5;p.UseDestructionFraction=false;
        foreach(var s in p.Sections){s.InitialVelocity=.5;s.MethodShape=1;s.MethodConnection=1;s.MethodCoefficientSource="Синтетический контроль формул: K=1, Nр=1; не определены по таблице и не относятся к реальному объекту.";}
        p.Notes="Синтетический пример для проверки вычислений методики 1997. Не исходный контрольный расчёт «Волны». Коэффициенты K=Nр=1 заданы явно только для демонстрации.";
        return p;
    }
}
