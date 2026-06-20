using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Content.Server.Station.Components;
using Content.Shared.Atmos;
using Content.Shared.GameTicking.Components;
using Content.Shared.Station.Components;
using Robust.Shared.Collections;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.GameTicking.Rules;

public abstract partial class GameRuleSystem<T> where T: IComponent
{
    protected EntityQueryEnumerator<ActiveGameRuleComponent, T, GameRuleComponent> QueryActiveRules()
    {
        return EntityQueryEnumerator<ActiveGameRuleComponent, T, GameRuleComponent>();
    }

    protected EntityQueryEnumerator<DelayedStartRuleComponent, T, GameRuleComponent> QueryDelayedRules()
    {
        return EntityQueryEnumerator<DelayedStartRuleComponent, T, GameRuleComponent>();
    }

    /// <summary>
    /// Queries all gamerules, regardless of if they're active or not.
    /// </summary>
    protected EntityQueryEnumerator<T, GameRuleComponent> QueryAllRules()
    {
        return EntityQueryEnumerator<T, GameRuleComponent>();
    }

    /// <summary>
    ///     Utility function for finding a random event-eligible station entity
    /// </summary>
    protected bool TryGetRandomStation([NotNullWhen(true)] out EntityUid? station, Func<EntityUid, bool>? filter = null)
    {
        var stations = new ValueList<EntityUid>(Count<StationEventEligibleComponent>());

        filter ??= _ => true;
        var query = AllEntityQuery<StationEventEligibleComponent>();

        while (query.MoveNext(out var uid, out _))
        {
            if (!filter(uid))
                continue;

            stations.Add(uid);
        }

        if (stations.Count == 0)
        {
            station = null;
            return false;
        }

        // TODO: Engine PR.
        station = stations[RobustRandom.Next(stations.Count)];
        return true;
    }

    protected bool TryFindRandomTile(out Vector2i tile,
        [NotNullWhen(true)] out EntityUid? targetStation,
        out EntityUid targetGrid,
        out EntityCoordinates targetCoords)
    {
        tile = default;
        targetStation = EntityUid.Invalid;
        targetGrid = EntityUid.Invalid;
        targetCoords = EntityCoordinates.Invalid;
        if (TryGetRandomStation(out targetStation))
        {
            return TryFindRandomTileOnStation((targetStation.Value, Comp<StationDataComponent>(targetStation.Value)),
                out tile,
                out targetGrid,
                out targetCoords);
        }

        return false;
    }

    protected bool TryFindRandomTileOnStation(Entity<StationDataComponent> station,
        out Vector2i tile,
        out EntityUid targetGrid,
        out EntityCoordinates targetCoords)
    {
        tile = default;
        targetCoords = EntityCoordinates.Invalid;
        targetGrid = EntityUid.Invalid;

        // Weight grid choice by valid tilecount (as judged by GridAtmosphere)
        var totalTiles = 0;
        var grids = new List<(Entity<MapGridComponent> Entity, ReadOnlyDictionary<Vector2i, TileAtmosphere> Tiles)>();
        foreach (var possibleTarget in station.Comp.Grids)
        {
            if (!_atmosphere.TryGetTiles(possibleTarget, out var tiles)
                || tiles.Count <= 0
                || !TryComp<MapGridComponent>(possibleTarget, out var mapGrid))
                continue;

            grids.Add(((possibleTarget, mapGrid), tiles));
            totalTiles += tiles.Count;
        }

        if (grids.Count <= 0)
            return false;

        // Pick a random tile index, find your starting dictionary from that.
        var startingTileIndex = RobustRandom.Next(totalTiles);
        var tilesSoFar = 0;
        var startingGridIndex = 0;
        for (var i = 0; i < grids.Count; i++)
        {
            if (tilesSoFar + grids[i].Tiles.Count > startingTileIndex)
            {
                startingGridIndex = i;
                startingTileIndex = startingTileIndex - tilesSoFar; // convert overall index to grid index
                break;
            }

            tilesSoFar += grids[i].Tiles.Count;
        }

        // Iterate from your starting position to the end of the grid set.
        for (var i = startingGridIndex; i < grids.Count; i++)
        {
            var iterator = grids[i].Tiles.GetEnumerator();
            // Start from tile index
            if (i == startingGridIndex)
            {
                for (var j = 0; j < startingTileIndex; j++)
                    iterator.MoveNext();
            }
            if (TryGetFirstPosition(iterator, out tile))
            {
                targetGrid = grids[i].Entity;
                targetCoords = _map.GridTileToLocal(targetGrid, grids[i].Entity.Comp, tile);
                return true;
            }
        }

        // Iterate from the start of the dict back to the starting position.
        for (var i = 0; i <= startingGridIndex; i++)
        {
            var iterator = grids[i].Tiles.GetEnumerator();
            if (i == startingGridIndex)
            {
                // Starting position: cover positions up to (not including) startingTileIndex
                var index = 0;
                while (index < startingTileIndex && iterator.MoveNext())
                {
                    if (iterator.Current.Value.Air != null)
                    {
                        targetGrid = grids[i].Entity;
                        tile = iterator.Current.Key;
                        targetCoords = _map.GridTileToLocal(targetGrid, grids[i].Entity.Comp, tile);
                        return true;
                    }
                    index++;
                }
            }
            else if (TryGetFirstPosition(iterator, out tile))
            {
                targetGrid = grids[i].Entity;
                targetCoords = _map.GridTileToLocal(targetGrid, grids[i].Entity.Comp, tile);
                return true;
            }
        }

        return false;
    }

    private bool TryGetFirstPosition(IEnumerator<KeyValuePair<Vector2i, TileAtmosphere>> iterator, out Vector2i vector)
    {
        while (iterator.MoveNext())
        {
            if (iterator.Current.Value.Air != null || iterator.Current.Value.Space)
            {
                vector = iterator.Current.Key;
                return true;
            }
        }

        vector = default;
        return false;
    }

    protected void ForceEndSelf(EntityUid uid, GameRuleComponent? component = null)
    {
        GameTicker.EndGameRule(uid, component);
    }
}
