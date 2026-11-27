using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = System.Random;

namespace TrashHunter
{
    internal sealed class SearchPile
    {
        public const int GeneratorVersion = 2;
        private const float GridStep = 0.10f;
        private const float Clearance = 0.02f;
        private const float SupportSearchHeight = 0.24f;

        private readonly Vector3 cabinetSize;
        private readonly (string Id, Vector3 Size) crate;
        private readonly WorldItem cabinetStart;
        private readonly (string Id, Vector3 Size, string[] Contents)[] packages;
        private readonly (string Id, Vector3 Size)[] looseFinds;
        private readonly Scene scene;
        private readonly Rect pileArea;
        private readonly float floorHeight;
        private readonly Collider[] hits = new Collider[32];
        private readonly RaycastHit[] supports = new RaycastHit[16];

        public SearchPile(SearchItem[] items, BoxCollider floorCollider, Vector3 entry)
        {
            // Capture the authored dimensions and cabinet pose before the finds move.
            var cabinet = items.Single(item => item.Id == "cabinet-a");
            cabinetStart = cabinet.LayoutPose(0);
            cabinetSize = cabinet.GetComponent<BoxCollider>().size;
            packages = items.Where(item => item.IsPackage)
                .Select(item => (item.Id, item.GetComponent<BoxCollider>().size,
                    item.InitialContents.Select(content => content.Id).ToArray())).ToArray();
            var nested = packages.SelectMany(package => package.Contents).ToHashSet();
            var finds = items.Where(item => !item.LegacyLayoutOnly && item != cabinet && !item.IsPackage &&
                    !nested.Contains(item.Id))
                .Select(item => (item.Id, Size: item.GetComponent<BoxCollider>().size)).ToArray();
            crate = finds.Single(find => find.Id == "crate-a");
            looseFinds = finds.Where(find => find.Id != crate.Id).ToArray();
            scene = floorCollider.gameObject.scene;
            var floor = floorCollider.bounds;
            floorHeight = floor.max.y;
            // Leave the entry landing and an aisle on the left. The cabinet faces the entry
            // from the right, with free space above and in front of it for the first pull.
            pileArea = Rect.MinMaxRect(entry.x - 0.62f, entry.z + 0.55f, floor.max.x - 0.30f, floor.max.z - 0.55f);
        }

        public WorldItem[] Generate(int seed)
        {
            var random = new Random(seed);
            // System.Random starts opposite signed seeds in the same state.
            if (seed < 0)
                random.Next();
            var placed = new List<Placement>(looseFinds.Length + packages.Length + 2);
            var cabinetPose = cabinetStart;
            cabinetPose.position += new Vector3(Between(random, -0.08f, 0.08f), 0, Between(random, -0.07f, 0.07f));
            cabinetPose.rotation *= Quaternion.Euler(0, Between(random, -12, 12), 0);
            placed.Add(new Placement(cabinetPose, Footprint(cabinetPose.position,
                HorizontalExtents(cabinetSize * 0.5f, cabinetPose.rotation)), cabinetSize.y));

            var focus = new Vector2(pileArea.center.x + Between(random, -0.38f, 0.38f),
                pileArea.center.y + Between(random, -0.50f, 0.50f));
            var cratePlacement = PlaceOnFloor(crate.Id, crate.Size, focus, random, placed);
            placed.Add(cratePlacement);

            var contents = new List<WorldItem>();
            for (var i = 0; i < packages.Length; i++)
            {
                var package = packages[i];
                var approach = new Vector2(pileArea.xMin + 0.45f + i * 1.1f, pileArea.yMin + 0.45f);
                var placement = PlaceOnFloor(package.Id, package.Size, approach, random, placed);
                placed.Add(placement);
                foreach (var id in package.Contents)
                {
                    var nested = placement.Pose;
                    nested.key = id;
                    nested.package = package.Id;
                    contents.Add(nested);
                }
            }

            var top = cratePlacement.Area;
            // Use the inscribed rectangle of the slightly rotated crate for reliable support.
            top = Rect.MinMaxRect(top.xMin + 0.11f, top.yMin + 0.11f, top.xMax - 0.11f, top.yMax - 0.11f);
            var stacked = 0;
            var finds = looseFinds
                .OrderByDescending(find => Mathf.Max(find.Size.x, find.Size.z) * Between(random, 0.80f, 1.20f));

            foreach (var find in finds)
            {
                var rotation = Quaternion.Euler(0f, Between(random, -180f, 180f), 0f);
                if (stacked < 3 && random.Next(2) == 0 &&
                    TryPlace(find.Id, find.Size, rotation, top, cratePlacement.Top, top.center, random, placed, out var upper))
                {
                    placed.Add(upper);
                    stacked++;
                }
                else
                {
                    placed.Add(PlaceOnFloor(find.Id, find.Size, focus, random, placed));
                }
            }
            return placed.Select(placement => placement.Pose).Concat(contents).ToArray();
        }

        private Placement PlaceOnFloor(string id, Vector3 size, Vector2 focus, Random random, List<Placement> placed)
        {
            var yaw = id == crate.Id ? Between(random, -10f, 10f) : Between(random, -180f, 180f);
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            if (TryPlace(id, size, rotation, pileArea, null, focus, random, placed, out var placement))
                return placement;

            // A lengthwise pose fits narrow gaps left by the earlier pieces.
            if (TryPlace(id, size, Quaternion.identity, pileArea, null, focus, random, placed, out placement) ||
                TryPlace(id, size, Quaternion.Euler(0, 90, 0), pileArea, null, focus, random, placed, out placement))
                return placement;

            throw new InvalidDataException($"Не удалось разместить предмет навала ({id}).");
        }

        private bool TryPlace(string id, Vector3 size, Quaternion rotation, Rect area, float? surface,
            Vector2 focus, Random random, List<Placement> placed, out Placement best)
        {
            best = default;
            var bestScore = float.PositiveInfinity;
            var halfSize = size * 0.5f;
            var extents = HorizontalExtents(halfSize, rotation);

            // A different grid origin and target per find break up rows and concentrate the heap.
            var offset = new Vector2(Between(random, 0, GridStep), Between(random, 0, GridStep));
            var target = focus + new Vector2(Between(random, -0.75f, 0.75f), Between(random, -0.65f, 0.65f));

            for (var x = area.xMin + extents.x + offset.x; x + extents.x <= area.xMax; x += GridStep)
            {
                for (var z = area.yMin + extents.y + offset.y; z + extents.y <= area.yMax; z += GridStep)
                {
                    var score = (new Vector2(x, z) - target).sqrMagnitude;
                    if (score >= bestScore)
                        continue;

                    var bottom = (surface ?? FindSurface(x, z)) + Clearance;
                    var y = bottom + halfSize.y;
                    var position = new Vector3(x, y, z);
                    var footprint = Footprint(position, extents + Vector2.one * Clearance);
                    if (OverlapsFind(footprint, bottom, y + halfSize.y, placed) || HitsScenery(position, halfSize, rotation))
                        continue;

                    bestScore = score;
                    best = new Placement(new WorldItem { key = id, position = position, rotation = rotation },
                        Footprint(position, extents), size.y);
                }
            }
            return !float.IsPositiveInfinity(bestScore);
        }

        private float FindSurface(float x, float z)
        {
            // During departure, the home scene is still loaded while the pile is prepared.
            var start = new Vector3(x, floorHeight + SupportSearchHeight, z);
            var count = Physics.RaycastNonAlloc(start, Vector3.down, supports, SupportSearchHeight,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var surface = floorHeight;
            for (var i = 0; i < count; i++)
            {
                var hit = supports[i];
                if (hit.collider.gameObject.scene == scene && hit.rigidbody == null && hit.normal.y > 0.99f)
                    surface = Mathf.Max(surface, hit.point.y);
            }
            return surface;
        }

        private static bool OverlapsFind(Rect footprint, float bottom, float top, List<Placement> placed)
        {
            foreach (var other in placed)
                if (bottom < other.Top && top > other.Bottom && footprint.Overlaps(other.Area))
                    return true;
            return false;
        }

        private bool HitsScenery(Vector3 position, Vector3 halfSize, Quaternion rotation)
        {
            var count = Physics.OverlapBoxNonAlloc(position, halfSize, hits, rotation,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
                if (hits[i].gameObject.scene == scene && hits[i].attachedRigidbody == null)
                    return true;
            return count == hits.Length;
        }

        private static Vector2 HorizontalExtents(Vector3 halfSize, Quaternion rotation)
        {
            var right = rotation * Vector3.right;
            var forward = rotation * Vector3.forward;
            return new Vector2(Mathf.Abs(right.x) * halfSize.x + Mathf.Abs(forward.x) * halfSize.z,
                Mathf.Abs(right.z) * halfSize.x + Mathf.Abs(forward.z) * halfSize.z);
        }

        private static Rect Footprint(Vector3 center, Vector2 extents) =>
            Rect.MinMaxRect(center.x - extents.x, center.z - extents.y, center.x + extents.x, center.z + extents.y);

        private static float Between(Random random, float min, float max) => min + (max - min) * (float)random.NextDouble();

        private readonly struct Placement
        {
            public readonly WorldItem Pose;
            public readonly Rect Area;
            public readonly float Bottom;
            public readonly float Top;

            public Placement(WorldItem pose, Rect area, float height)
            {
                Pose = pose;
                Area = area;
                Bottom = pose.position.y - height * 0.5f;
                Top = pose.position.y + height * 0.5f;
            }
        }
    }
}
