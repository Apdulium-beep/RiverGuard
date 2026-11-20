using System.Text.Json.Serialization;

namespace RiverGuard.Core;

// Настраиваемые сценарные критерии зон
public sealed class ZoneCriteria : Observable
{
    private double floodDepth,dangerDepth=1.5,destructionDepth=3,alternativeDepth=1,alternativeVelocity=0.7,destructionArrivalHours=1;
    private bool useDepthAndVelocity;
    public double FloodDepth {get=>floodDepth;set=>Set(ref floodDepth,value);}
    public double DangerDepth {get=>dangerDepth;set=>Set(ref dangerDepth,value);}
    public double DestructionDepth {get=>destructionDepth;set=>Set(ref destructionDepth,value);}
    public bool UseDepthAndVelocity {get=>useDepthAndVelocity;set=>Set(ref useDepthAndVelocity,value);}
    public double AlternativeDepth {get=>alternativeDepth;set=>Set(ref alternativeDepth,value);}
    public double AlternativeVelocity {get=>alternativeVelocity;set=>Set(ref alternativeVelocity,value);}
    public double DestructionArrivalHours {get=>destructionArrivalHours;set=>Set(ref destructionArrivalHours,value);}
    [JsonIgnore] public string Description=>$"ВЗМ: глубина ≥ {FloodDepth:F3} м; ВКЗМ: "+(UseDepthAndVelocity?$"глубина ≥ {AlternativeDepth:F3} м И скорость ≥ {AlternativeVelocity:F3} м/с":$"глубина ≥ {DangerDepth:F3} м")+$"; КР: глубина > {DestructionDepth:F3} м и приход фронта не позже {DestructionArrivalHours:F3} ч от начала аварии. Сценарная классификация, не подтверждение нормативного соответствия.";
}

// Геометрические границы по выбранному критерию
public static class ZoneEvaluation
{
    public static double EffectiveDangerDepth(ZoneCriteria criteria,double dischargeAtLevelPeak,double conveyance)
    {
        if(!criteria.UseDepthAndVelocity)return criteria.DangerDepth;
        double coefficient=Math.Abs(dischargeAtLevelPeak)/Math.Max(conveyance,1e-8);
        double speedDepth=criteria.AlternativeVelocity<=0?0:coefficient>0?Math.Pow(criteria.AlternativeVelocity/coefficient,1.5):double.PositiveInfinity;
        return Math.Max(criteria.FloodDepth,Math.Max(criteria.AlternativeDepth,speedDepth));
    }
    public static bool DestructionReached(ZoneCriteria criteria,double? arrivalHours)=>arrivalHours.HasValue&&arrivalHours.Value<=criteria.DestructionArrivalHours;
}
