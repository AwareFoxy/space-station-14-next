using Robust.Server.GameObjects;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server.Station.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Server.Maps;
using Robust.Shared.Random;
using Content.Shared.Ghost;
using Content.Server._CorvaxNext.Ghostbar.Components;
using Content.Server.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Roles;
using Content.Server.Antag.Components;
using Content.Shared.Mindshield.Components;
using Content.Shared.Players;

namespace Content.Server._CorvaxNext.Ghostbar;

public sealed class GhostBarSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly StationSpawningSystem _spawningSystem = default!;
    [Dependency] private readonly MindSystem _mindSystem = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;

    private static readonly List<JobComponent> _jobComponents = new()
    {
        new JobComponent { Prototype = "Passenger" },
        new JobComponent { Prototype = "Bartender" },
        new JobComponent { Prototype = "Botanist" },
        new JobComponent { Prototype = "Chef" },
        new JobComponent { Prototype = "Janitor" }
    };

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStart);
        SubscribeNetworkEvent<GhostBarSpawnEvent>(SpawnPlayer);
        SubscribeLocalEvent<GhostBarPlayerComponent, MindRemovedMessage>(OnPlayerGhosted);
    }

    const string MapPath = "Maps/_CorvaxNext/Nonstations/ghostbar.yml";
    private void OnRoundStart(RoundStartingEvent ev)
    {
        _mapSystem.CreateMap(out var mapId);
        var options = new MapLoadOptions { LoadMap = true };

        if (_mapLoader.TryLoad(mapId, MapPath, out _, options))
            _mapSystem.SetPaused(mapId, false);
    }

    public void SpawnPlayer(GhostBarSpawnEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;

        if (!_entityManager.HasComponent<GhostComponent>(player.AttachedEntity))
        {
            Log.Warning($"User {player.Name} tried to spawn at ghost bar without being a ghost.");
            return;
        }

        var spawnPoints = new List<EntityCoordinates>();
        var query = EntityQueryEnumerator<GhostBarSpawnComponent>();
        while (query.MoveNext(out var ent, out _))
        {
            spawnPoints.Add(_entityManager.GetComponent<TransformComponent>(ent).Coordinates);
        }

        if (spawnPoints.Count == 0)
        {
            Log.Warning("No spawn points found for ghost bar.");
            return;
        }

        var data = player.ContentData();

        if (data == null)
        {
            Log.Warning($"ContentData was null when trying to spawn {player.Name} in ghost bar.");
            return;
        }

        var randomSpawnPoint = _random.Pick(spawnPoints);
        var randomJob = _random.Pick(_jobComponents);
        var profile = _ticker.GetPlayerProfile(args.SenderSession);
        var mobUid = _spawningSystem.SpawnPlayerMob(randomSpawnPoint, randomJob, profile, null);

        _entityManager.EnsureComponent<GhostBarPlayerComponent>(mobUid);
        _entityManager.EnsureComponent<MindShieldComponent>(mobUid);
        _entityManager.EnsureComponent<AntagImmuneComponent>(mobUid);
        _entityManager.EnsureComponent<IsDeadICComponent>(mobUid);

        var newMind = _mindSystem.CreateMind(data.UserId, profile.Name);
        _mindSystem.TransferTo(newMind, mobUid, true);
    }

    private void OnPlayerGhosted(EntityUid uid, GhostBarPlayerComponent component, MindRemovedMessage args)
    {
        _entityManager.DeleteEntity(uid);
    }
}

