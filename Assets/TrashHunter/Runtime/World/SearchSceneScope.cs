using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using VContainer;
using VContainer.Unity;

namespace TrashHunter
{
    public sealed class SearchSceneScope : LifetimeScope
    {
        [SerializeField] private RunLocation location;
        [SerializeField] private Transform entry;
        [SerializeField] private SearchSession session;
        [SerializeField] private SearchPlayer playerPrefab;
        [SerializeField] private SearchHud hudPrefab;

        private readonly List<GameObject> eventSystems = new();

        internal RunLocation Location => location;
        internal SearchSession Session => session;
        private Transform Entry => location == RunLocation.Search ? session.SearchEntry : entry;
        internal PlayerPose EntryPose => SearchPlayer.PoseAt(Entry);

        protected override void Awake()
        {
            autoRun = false;
            base.Awake();
            // A same-scene reload briefly has both copies loaded additively.
            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                if (root.activeSelf && root.TryGetComponent<EventSystem>(out _))
                {
                    eventSystems.Add(root);
                    root.SetActive(false);
                }
            }
            if (location == RunLocation.Search && session != null)
                session.StageSearch();
        }

        internal void Mount()
        {
            Build();
            foreach (var input in eventSystems)
                input.SetActive(true);
        }

        protected override LifetimeScope FindParent() => RunBootstrap.RunScope;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterBuildCallback(resolver =>
            {
                var player = resolver.Instantiate(playerPrefab, transform);
                var hud = resolver.Instantiate(hudPrefab, transform);
                session.Initialize(resolver.Resolve<GameState>(), resolver.Resolve<LootCatalog>(),
                    RunBootstrap.Scenes, this, player, hud);
            });
        }

        internal bool IsConfiguredFor(RunLocation expected) =>
            location == expected && isActiveAndEnabled && eventSystems.Count <= 1 && session != null && Entry != null &&
            playerPrefab != null && hudPrefab != null && (location != RunLocation.Search || session.HasSearchSetup);
    }
}
