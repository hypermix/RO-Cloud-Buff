namespace RoBuff.Core;

// Explicit observation rounds; never infer the caster from an outgoing target ID.
public sealed class IdentityCalibration
{
    readonly Dictionary<(uint Actor,int Status),StatusEvent> candidates=[];
    const int ObservationSeconds=90;
    HashSet<uint>? allowed;
    public DateTimeOffset? Until { get; private set; }
    public bool Started => Until is not null;
    public void Start(DateTimeOffset now,bool narrow)
    {
        if(narrow&&candidates.Count==0)throw new InvalidOperationException("좁힐 후보가 없습니다. 처음부터 다시 확인해주세요.");
        allowed=narrow?candidates.Keys.Select(k=>k.Actor).ToHashSet():null;
        candidates.Clear();Until=now.AddSeconds(ObservationSeconds);
    }
    public void Observe(StatusEvent value)
    {
        if(Until is not { } until||value.Time>until||value.Time<until.AddSeconds(-ObservationSeconds)||!value.Active)return;
        if(allowed is not null&&!allowed.Contains(value.ActorId))return;
        var key=(value.ActorId,value.StatusId);
        if(candidates.Count<1000||candidates.ContainsKey(key))candidates[key]=value;
    }
    public IReadOnlyCollection<StatusEvent> Candidates(DateTimeOffset now) =>
        Until is { } until&&now<=until.AddMinutes(2)?candidates.Values.ToArray():[];
    public bool CanConfirm(uint actor,DateTimeOffset now)=>Candidates(now).Any(e=>e.ActorId==actor);
    public void Reset(){candidates.Clear();allowed=null;Until=null;}
}
