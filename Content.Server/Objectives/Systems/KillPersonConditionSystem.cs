using System.Linq; // Corvax-Next-Centcomm
using Content.Server.Objectives.Components;
using Content.Server.Revolutionary.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Components; // Corvax-Next-Centcomm
using Content.Shared.CCVar;
using Content.Shared.Mind;
using Content.Shared.Objectives.Components;
using Content.Shared.Roles; // Corvax-Next-Centcomm
using Content.Shared.Roles.Jobs; // Corvax-Next-Centcomm
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes; // Corvax-Next-Centcomm
using Robust.Shared.Random;

namespace Content.Server.Objectives.Systems;

/// <summary>
/// Handles kill person condition logic and picking random kill targets.
/// </summary>
public sealed class KillPersonConditionSystem : EntitySystem
{
    [Dependency] private readonly EmergencyShuttleSystem _emergencyShuttle = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly TargetObjectiveSystem _target = default!;
    [Dependency] private readonly SharedRoleSystem _roleSystem = default!; // Corvax-Next-Centcomm
    [Dependency] private readonly IPrototypeManager _prototype = default!; // Corvax-Next-Centcomm

    private static readonly ProtoId<DepartmentPrototype> _ccDep = "CentralCommandCorvax"; // Corvax-Next-Centcomm

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KillPersonConditionComponent, ObjectiveGetProgressEvent>(OnGetProgress);

        SubscribeLocalEvent<PickRandomPersonComponent, ObjectiveAssignedEvent>(OnPersonAssigned);

        SubscribeLocalEvent<PickRandomHeadComponent, ObjectiveAssignedEvent>(OnHeadAssigned);
    }

    private void OnGetProgress(EntityUid uid, KillPersonConditionComponent comp, ref ObjectiveGetProgressEvent args)
    {
        if (!_target.GetTarget(uid, out var target))
            return;

        args.Progress = GetProgress(target.Value, comp.RequireDead);
    }

    private void OnPersonAssigned(EntityUid uid, PickRandomPersonComponent comp, ref ObjectiveAssignedEvent args)
    {
        // invalid objective prototype
        if (!TryComp<TargetObjectiveComponent>(uid, out var target))
        {
            args.Cancelled = true;
            return;
        }

        // target already assigned
        if (target.Target != null)
            return;

        // no other humans to kill
        var allHumans = _mind.GetAliveHumans(args.MindId);
        if (allHumans.Count == 0)
        {
            args.Cancelled = true;
            return;
        }

        // Corvax-Next-Centcomm-Start
        FilterCentCom(allHumans);

        if (allHumans.Count == 0)
        {
            args.Cancelled = true;
            return;
        }
        // Corvax-Next-Centcomm-End

        _target.SetTarget(uid, _random.Pick(allHumans), target);

    }

        // Corvax-Next-Centcomm-Start
    private void FilterCentCom(List<EntityUid> minds)
    {
        var centcom = _prototype.Index(_ccDep);
        foreach (var mindId in minds.ToArray())
        {
            if (!_roleSystem.MindHasRole<JobRoleComponent>(mindId, out var job) || job.Value.Comp1.JobPrototype == null)
            {
                continue;
            }

            if (!centcom.Roles.Contains(job.Value.Comp1.JobPrototype.Value))
            {
                continue;
            }

            minds.Remove(mindId);
        }
    }
        // Corvax-Next-Centcomm-End

    private void OnHeadAssigned(EntityUid uid, PickRandomHeadComponent comp, ref ObjectiveAssignedEvent args)
    {
        // invalid prototype
        if (!TryComp<TargetObjectiveComponent>(uid, out var target))
        {
            args.Cancelled = true;
            return;
        }

        // target already assigned
        if (target.Target != null)
            return;

        // no other humans to kill
        var allHumans = _mind.GetAliveHumans(args.MindId);
        if (allHumans.Count == 0)
        {
            args.Cancelled = true;
            return;
        }

        // Corvax-Next-Centcomm-Start
        FilterCentCom(allHumans);

        if (allHumans.Count == 0)
        {
            args.Cancelled = true;
            return;
        }
        // Corvax-Next-Centcomm-End

        var allHeads = new HashSet<Entity<MindComponent>>();
        foreach (var person in allHumans)
        {
            if (TryComp<MindComponent>(person, out var mind) && mind.OwnedEntity is { } ent && HasComp<CommandStaffComponent>(ent))
                allHeads.Add(person);
        }

        if (allHeads.Count == 0)
            allHeads = allHumans; // fallback to non-head target

        _target.SetTarget(uid, _random.Pick(allHeads), target);
    }

    private float GetProgress(EntityUid target, bool requireDead)
    {
        // deleted or gibbed or something, counts as dead
        if (!TryComp<MindComponent>(target, out var mind) || mind.OwnedEntity == null)
            return 1f;

        // dead is success
        if (_mind.IsCharacterDeadIc(mind))
            return 1f;

        // if the target has to be dead dead then don't check evac stuff
        if (requireDead)
            return 0f;

        // if evac is disabled then they really do have to be dead
        if (!_config.GetCVar(CCVars.EmergencyShuttleEnabled))
            return 0f;

        // target is escaping so you fail
        if (_emergencyShuttle.IsTargetEscaping(mind.OwnedEntity.Value))
            return 0f;

        // evac has left without the target, greentext since the target is afk in space with a full oxygen tank and coordinates off.
        if (_emergencyShuttle.ShuttlesLeft)
            return 1f;

        // if evac is still here and target hasn't boarded, show 50% to give you an indicator that you are doing good
        return _emergencyShuttle.EmergencyShuttleArrived ? 0.5f : 0f;
    }
}
