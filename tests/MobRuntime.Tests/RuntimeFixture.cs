using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;

namespace MobRuntime.Tests
{
    internal sealed class PackFixture : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "mob-runtime-tests-" + Guid.NewGuid().ToString("N"));
        public MobModelManifest Manifest;
        public readonly string PrefabPath = "assets/models/crab.prefab";
        public Action<GameObject, AssetBundle> AlterBundle;
        public string BundlePath => Path.Combine(Root, "models", "fixture.bundle");
        public PackFixture(string contentVersion = "1.0", string modelId = "fixture-crab")
        {
            Directory.CreateDirectory(Path.Combine(Root, "models"));
            File.WriteAllBytes(BundlePath, new byte[] { 1, 2, 3, 4, 5 });
            Manifest = new MobModelManifest
            {
                SchemaVersion = 1, PackId = "fixture-pack", ContentVersion = contentVersion,
                UnityVersion = Application.unityVersion, GameVersion = Application.version,
                BuildTarget = "StandaloneWindows64", BundleFile = "fixture.bundle",
                BundleSize = new FileInfo(BundlePath).Length,
                Models = new[] { new MobModelEntry { Id = modelId, Prefab = PrefabPath,
                    BaseMonsterTypes = new[] { nameof(Mon_Fixture_Crab) } } }
            };
            SaveManifest();
            AssetBundle.Factories[BundlePath] = CreateBundle;
        }
        public void SaveManifest() => File.WriteAllText(Path.Combine(Root, "models", "manifest.json"), JsonConvert.SerializeObject(Manifest));
        private AssetBundle CreateBundle()
        {
            var bundle = new AssetBundle();
            var prefab = new GameObject("fixture-prefab");
            var model = prefab.AddComponent<EntityModel>();
            var body = new GameObject("body"); prefab.AddChild(body);
            var renderer = body.AddComponent<MeshRenderer>();
            body.AddComponent<MeshFilter>().sharedMesh = new Mesh();
            renderer.sharedMaterials = new[] { new Material { shader = new Shader() } };
            model.bodyRenderers = new Renderer[] { renderer };
            model.healthBarPosition = body.transform;
            var clip = new AnimationClip { name = "fixture-idle" };
            model.idle = model.stagger = model.death = new AnimationClipWithSpeed { clip = clip, speed = 1 };
            model.runForwardClip = clip;
            var animator = prefab.AddComponent<Animator>();
            animator.avatar = new Avatar();
            bundle.Assets[PrefabPath] = prefab;
            bundle.Assets["fixture-animation"] = clip;
            AlterBundle?.Invoke(prefab, bundle);
            return bundle;
        }
        public void Dispose()
        {
            AssetBundle.Factories.Remove(BundlePath);
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }

    internal sealed class Endpoint : IDisposable
    {
        public readonly Simulation World;
        public readonly DewPlayer Player = new DewPlayer();
        public readonly ActorManager Manager = new ActorManager();
        public readonly ZoneManager Zone = new ZoneManager();
        public readonly bool IsHost;
        public MobModelSession Session;
        public readonly List<object> Received = new List<object>();
        public Endpoint(Simulation world, bool host) { World = world; IsHost = host; }
        public void Run(Action action)
        {
            Mirror.NetworkServer.active = IsHost; Mirror.NetworkClient.active = true;
            DewPlayer.local = Player; DewPlayer.gamePlayers = World.Players;
            NetworkedManagerBase<ActorManager>.softInstance = Manager;
            NetworkedManagerBase<ZoneManager>.softInstance = Zone;
            action();
        }
        public void Start(string path, bool enabled = true) => Run(() => Session = new MobModelSession(path, enabled));
        public Mon_Fixture_Crab AddMonster(uint id)
        {
            var monster = new Mon_Fixture_Crab { netId = id, owner = World.Creep };
            var native = new EntityModel { isInitialized = true, name = "native-" + id };
            monster.Visual = new EntityVisual { model = native, NativeModel = native };
            Run(() => Manager.Add(monster));
            return monster;
        }
        public void Tick() => Run(() => Session?.Tick());
        public void Receive(object message, DewPlayer caller)
        {
            Received.Add(message);
            Run(() => { if (caller == null) Manager.serverActor.ReceiveClient(message); else Manager.serverActor.ReceiveServer(message, caller); });
        }
        public void Dispose() => Run(() => Session?.Dispose());
    }

    internal sealed class Simulation : IDisposable
    {
        public readonly List<DewPlayer> Players = new List<DewPlayer>();
        public readonly DewPlayer Creep = new DewPlayer { isHumanPlayer = false };
        public readonly List<Endpoint> Endpoints = new List<Endpoint>();
        public readonly Queue<Action> Messages = new Queue<Action>();
        public Endpoint Host;
        public Simulation()
        {
            Time.unscaledTime = 0; AssetBundle.Loaded.Clear(); AssetBundle.LoadAttempts = 0; Log.Warnings.Clear();
        }
        public Endpoint AddEndpoint(bool host, string path, bool enabled = true)
        {
            var endpoint = new Endpoint(this, host); Endpoints.Add(endpoint); Players.Add(endpoint.Player);
            if (host) Host = endpoint;
            endpoint.Manager.serverActor.SendServer = message => Messages.Enqueue(() => Host.Receive(message, endpoint.Player));
            endpoint.Manager.serverActor.SendClient = (player, message) =>
            {
                var recipient = Endpoints.SingleOrDefault(e => e.Player == player);
                if (recipient != null) Messages.Enqueue(() => recipient.Receive(message, null));
            };
            endpoint.Manager.serverActor.SendAll = message =>
            {
                foreach (var recipient in Endpoints.Where(e => e != endpoint))
                    Messages.Enqueue(() => recipient.Receive(message, null));
            };
            endpoint.Start(path, enabled); return endpoint;
        }
        public void Pump()
        {
            int safety = 0;
            while (Messages.Count > 0)
            {
                if (++safety > 1000) throw new InvalidOperationException("RPC loop in fixture");
                Messages.Dequeue()();
            }
        }
        public void Advance(int rounds = 1, bool tickClients = true)
        {
            for (int i = 0; i < rounds; i++)
            {
                Host?.Tick();
                if (tickClients) foreach (var endpoint in Endpoints.Where(e => !e.IsHost)) endpoint.Tick();
                Pump(); Time.unscaledTime += 1f;
            }
        }
        public void Dispose()
        {
            foreach (var endpoint in Endpoints) endpoint.Dispose();
            Messages.Clear();
            NetworkedManagerBase<ActorManager>.softInstance = null;
            NetworkedManagerBase<ZoneManager>.softInstance = null;
            Mirror.NetworkServer.active = Mirror.NetworkClient.active = false;
            DewPlayer.local = null; DewPlayer.gamePlayers = new List<DewPlayer>();
        }
    }
}
