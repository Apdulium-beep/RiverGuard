namespace RiverGuard.Core;

// Расход через аварийное отверстие
public static class SourceDischarge
{
    private const double G=9.81;
    public static double Evaluate(ProjectData p,double upperLevel,double lowerLevel,double seconds)
    {
        if(upperLevel<=p.SillElevation||upperLevel<=lowerLevel)return 0;
        double opening=p.FormationMinutes<=0?1:Math.Clamp(seconds/(p.FormationMinutes*60),0,1);
        if(ScenarioMode.IsGate(p.Scenario))
        {
            double bottom=p.SillElevation,top=Math.Min(bottom+p.GateHeight*opening,upperLevel);
            double submerged=Math.Max(0,Math.Min(top,lowerLevel)-bottom)*Math.Sqrt(upperLevel-lowerLevel);
            double freeBottom=Math.Max(bottom,lowerLevel);
            double free=top>freeBottom?2d/3*(Math.Pow(upperLevel-freeBottom,1.5)-Math.Pow(upperLevel-top,1.5)):0;
            return p.DischargeCoefficient*p.BreachWidth*Math.Sqrt(2*G)*(submerged+free);
        }
        if(ScenarioMode.IsCircular(p.Scenario))
        {
            double d=p.OutletDiameter;if(d<=0)return 0;
            double angle(double level)=>Math.Acos(Math.Clamp(1-2*(level-p.SillElevation)/d,-1,1));
            double wet=angle(upperLevel),split=Math.Min(wet,angle(lowerLevel)),radius=d/2;
            double Integrate(double start,double end)
            {
                const int count=64;double step=1d/count,sum=0;
                for(int i=0;i<=count;i++)
                {
                    double t=i*step,a=end-(end-start)*(1-t)*(1-t),jacobian=2*(end-start)*(1-t),z=p.SillElevation+radius*(1-Math.Cos(a));
                    double head=Math.Max(0,upperLevel-z)-Math.Max(0,lowerLevel-z);
                    double f=2*radius*radius*Math.Pow(Math.Sin(a),2)*Math.Sqrt(Math.Max(0,head));
                    sum+=(i==0||i==count?1:i%2==0?2:4)*f*jacobian;
                }
                return sum*step/3;
            }
            double submerged=radius*radius*(split-Math.Sin(split)*Math.Cos(split))*Math.Sqrt(upperLevel-lowerLevel);
            return p.DischargeCoefficient*Math.Sqrt(2*G)*(submerged+Integrate(split,wet));
        }
        double hu=Math.Max(0,upperLevel-p.SillElevation),hd=Math.Max(0,lowerLevel-p.SillElevation);
        return p.DischargeCoefficient*p.BreachWidth*(ScenarioMode.IsOverflow(p.Scenario)?1:opening)*Math.Sqrt(2*G)*Math.Max(0,Math.Pow(hu,1.5)-Math.Pow(hd,1.5));
    }
}
