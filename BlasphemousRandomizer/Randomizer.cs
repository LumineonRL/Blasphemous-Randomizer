﻿using UnityEngine;
using Gameplay.UI;
using System.Diagnostics;
using System.Collections;
using BlasphemousRandomizer.BossRando;
using BlasphemousRandomizer.DoorRando;
using BlasphemousRandomizer.EnemyRando;
using BlasphemousRandomizer.HintRando;
using BlasphemousRandomizer.ItemRando;
using BlasphemousRandomizer.Tracker;
using BlasphemousRandomizer.Settings;
using BlasphemousRandomizer.Map;
using Framework.Managers;
using Framework.Audio;
using Tools.Level;
using ModdingAPI;

namespace BlasphemousRandomizer
{
    public class Randomizer : PersistentMod
    {
        public const int MAX_SEED = 99_999_999;
        private const bool SKIP_CUTSCENES = true;

        // Shufflers
        public ItemShuffle itemShuffler;
        public EnemyShuffle enemyShuffler;
        public BossShuffle bossShuffler;
        public DoorShuffle doorShuffler;
        public HintShuffle hintShuffler;
        private IShuffle[] shufflers;

        // Save file info
        private int seed;
        private bool startedInRando;
        public Config gameConfig;

        // Global info
        private bool inGame;
        private int lastLoadedSlot;
        private string errorOnLoad;
        public bool ShrineEditMode { get; set; }

        public DataStorage data { get; private set; }
        public AutoTracker tracker { get; private set; }
        public MapCollectionStatus MapCollection { get; private set; }
        public SettingsMenu settingsMenu;

        public override string PersistentID => "ID_RANDOMIZER";

        public bool InstalledBootsMod => IsModLoaded("com.author.blasphemous.boots-of-pleading");
        public bool InstalledDoubleJumpMod => IsModLoaded("com.damocles.blasphemous.double-jump");

        public Randomizer(string modId, string modName, string modVersion) : base(modId, modName, modVersion) { }

        protected override void Initialize()
        {
            // Create main shufflers
            itemShuffler = new ItemShuffle();
            enemyShuffler = new EnemyShuffle();
            bossShuffler = new BossShuffle();
            doorShuffler = new DoorShuffle();
            hintShuffler = new HintShuffle();
            shufflers = new IShuffle[] { itemShuffler, enemyShuffler, bossShuffler, doorShuffler, hintShuffler };
            for (int i = 0; i < shufflers.Length; i++)
            {
                shufflers[i].Init();
            }

            // Load external data
            Log("Randomizer has been initialized!");
            data = new DataStorage();
            if (!data.loadData(FileUtil))
                errorOnLoad = Localize("daterr");

            // Set up data
            gameConfig = new Config();
            lastLoadedSlot = -1;
            settingsMenu = new SettingsMenu();
            MapCollection = new MapCollectionStatus();

            tracker = new AutoTracker();
            RegisterCommand(new RandomizerCommand());
        }

        public override ModPersistentData SaveGame()
        {
            return new RandomizerPersistenceData
            {
                seed = seed,
                startedInRando = startedInRando,
                config = gameConfig,
                collectionStatus = MapCollection.CollectionStatus
            };
        }

        public override void LoadGame(ModPersistentData data)
        {
            // Only reload data if coming from title screen and loading different save file
            if (inGame || PersistentManager.GetAutomaticSlot() == lastLoadedSlot)
            {
                return;
            }

            RandomizerPersistenceData randomizerPersistenceData = data == null ? null : (RandomizerPersistenceData)data;
            if (randomizerPersistenceData != null && randomizerPersistenceData.startedInRando && isConfigVersionValid(randomizerPersistenceData.config.VersionCreated))
            {
                // Loaded a valid randomized game
                seed = randomizerPersistenceData.seed;
                startedInRando = randomizerPersistenceData.startedInRando;
                gameConfig = randomizerPersistenceData.config;
                MapCollection.CollectionStatus = randomizerPersistenceData.collectionStatus;
                Log("Loading seed: " + seed);
                Randomize(false);
            }
            else
            {
                // Loaded a vanilla game or an outdated rando game
                seed = -1;
                startedInRando = false;
                gameConfig = new Config();
                MapCollection.ResetCollectionStatus(gameConfig);
                for (int i = 0; i < shufflers.Length; i++)
                {
                    shufflers[i].Reset();
                }
                LogError("Loaded invalid game!");
                errorOnLoad = Localize("vererr");
            }

            inGame = true;
            lastLoadedSlot = PersistentManager.GetAutomaticSlot();
        }

        public override void NewGame(bool NGPlus)
        {
            startedInRando = true;
            seed = generateSeed();
            setUpExtras();
            Log("Generating new seed: " + seed);
            Randomize(true);
            MapCollection.ResetCollectionStatus(gameConfig);

            inGame = true;
            lastLoadedSlot = PersistentManager.GetAutomaticSlot();
            Core.GameModeManager.ChangeMode(GameModeManager.GAME_MODES.NEW_GAME_PLUS);
            Core.Events.SetFlag("CHERUB_RESPAWN", true);
        }

        public override void ResetGame() { }

        private int generateSeed()
        {
            return gameConfig.CustomSeed > 0 ? gameConfig.CustomSeed : new System.Random().Next(1, 999999);
        }

        private void Randomize(bool newGame)
        {
            Stopwatch watch = Stopwatch.StartNew();

            // Shuffle everything
            for (int i = 0; i < shufflers.Length; i++)
            {
                try
                {
                    shufflers[i].Shuffle(seed);
                }
                catch (System.Exception)
                {
                    LogError($"Error with the {shufflers[i].GetType().Name} when shuffling seed {seed}");
                }
            }

            // Show error message if item shuffler failed
            if (!itemShuffler.validSeed)
                errorOnLoad = Localize("generr");

            // Generate spoiler on new game
            if (newGame)
            {
                string spoiler = itemShuffler.GetSpoiler();
                FileUtil.saveTextFile($"spoiler{PersistentManager.GetAutomaticSlot() + 1}.txt", spoiler);
            }
            watch.Stop();
            Log("Time to fill seed: " + watch.ElapsedMilliseconds + " ms");
        }

        // Specific actions need to be taken when a certain scene is loaded
        protected override void LevelLoaded(string oldLevel, string newLevel)
        {
            // Set gameplay status
            if (newLevel == "MainMenu")
                inGame = false;

            // Update ui menus
            if (settingsMenu != null)
                settingsMenu.onLoad(newLevel);

            // Display delayed error message
            if (errorOnLoad != null && errorOnLoad != "")
                UIController.instance.StartCoroutine(showErrorMessage(2.1f));

            // Misc functions
            EnemyLoader.loadEnemies(); // Load enemies
            updateShops(); // Update shop menus
            bossShuffler.levelLoaded(newLevel); // Spawn boss stuff
            tracker.LevelLoaded(newLevel);

            // Reload enemy audio catalogs
            AudioLoader audio = Object.FindObjectOfType<AudioLoader>();
            if (audio != null)
            {
                enemyShuffler.audioCatalogs = new FMODAudioCatalog[audio.AudioCatalogs.Length];
                audio.AudioCatalogs.CopyTo(enemyShuffler.audioCatalogs, 0);
                GameObject obj = audio.gameObject;
                Object.Destroy(audio);
                obj.AddComponent<AudioLoader>();
            }

            // Give first item when starting a new game
            if (newLevel == StartingDoor.Room)
            {
                itemShuffler.giveItem("QI106", true);
            }
        }

        // Set up a new game
        private void setUpExtras()
        {
            // Set flags relating to choosing a penitence
            if (!gameConfig.AllowPenitence)
            {
                Core.Events.SetFlag("PENITENCE_EVENT_FINISHED", true, false);
                Core.Events.SetFlag("PENITENCE_NO_PENITENCE", true, false);
            }
            // Set flags relating to various cutscenes
            if (SKIP_CUTSCENES)
            {
                foreach (string id in data.cutsceneFlags)
                {
                    Core.Events.SetFlag(id, true, false);
                }
            }
            // Set randomized flags
            string majorVersion = Main.MOD_VERSION;
            majorVersion = majorVersion.Substring(0, majorVersion.LastIndexOf('.'));
            Core.Events.SetFlag("RANDOMIZED", true, false);
            Core.Events.SetFlag(majorVersion, true, false);
        }

        // Update candelaria shops when opened or when purchased
        public void updateShops()
        {
            string scene = Core.LevelManager.currentLevel.LevelName;
            // Shop scenes - search for each item pedestal
            if (scene == "D02BZ02S01" || scene == "D01BZ02S01" || scene == "D05BZ02S01")
            {
                foreach (Interactable interactable in Object.FindObjectsOfType<Interactable>())
                {
                    if (data.interactableIds.ContainsKey(interactable.GetPersistenID()))
                    {
                        SpriteRenderer render = interactable.transform.parent.GetChild(0).GetChild(0).GetComponent<SpriteRenderer>();
                        if (render != null)
                        {
                            Item item = Main.Randomizer.itemShuffler.getItemAtLocation(data.interactableIds[interactable.GetPersistenID()]);
                            render.sprite = item == null ? null : item.getRewardInfo(true).sprite;
                        }
                    }
                }
            }
        }

        // Keyboard input
        protected override void Update()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.Keypad6) && inGame)
            {
                LogDisplay($"{Localize("currsd")}: {seed} [{ComputeFinalSeed(seed, gameConfig)}]");
            }
            else if (UnityEngine.Input.GetKeyDown(KeyCode.Keypad1))
            {
                //enemyShuffler.Shuffle(new System.Random().Next());
                //UIController.instance.ShowPopUp("Shuffling enemies temporarily!", "", 0, false);
            }
            else if (UnityEngine.Input.GetKeyDown(KeyCode.Keypad2))
            {
                //gameConfig.StartingLocation = 0;
                //Log("Starting from brotherhood");
            }
            else if (UnityEngine.Input.GetKeyDown(KeyCode.Keypad3))
            {
                //gameConfig.StartingLocation = 1;
                //Log("Starting from knot");
            }
            
            // Update ui menus
            if (settingsMenu != null)
                settingsMenu.update();
        }

        private IEnumerator showErrorMessage(float waitTime)
        {
            yield return new WaitForSecondsRealtime(waitTime);
            LogDisplay(errorOnLoad);
            errorOnLoad = "";
        }

        public bool shouldSkipCutscene(string id)
        {
            return SKIP_CUTSCENES && Main.arrayContains(data.cutsceneNames, id);
        }

        public void playSoundEffect(int id)
        {
            if (id == 0) Core.Audio.PlayOneShot("event:/SFX/UI/EquipItem");
            else if (id == 1) Core.Audio.PlayOneShot("event:/SFX/UI/UnequipItem");
            else if (id == 2) Core.Audio.PlayOneShot("event:/SFX/UI/ChangeSelection");
            else if (id == 3) Core.Audio.PlayOneShot("event:/SFX/UI/FadeToWhite");
        }

        private bool isConfigVersionValid(string configVersion)
        {
            string version = Main.MOD_VERSION;
            return version.Substring(0, version.LastIndexOf('.')) == configVersion.Substring(0, configVersion.LastIndexOf('.'));
        }

        public int GetSeed() { return seed; }

        public long ComputeFinalSeed(int seed, Config config)
        {
            // Generate unique int32 based on seed and important options
            long uniqueSeed = 0;

            // Remaining bits: 24, 25, 26, 27, 28, 29, 30, 31
            //uniqueSeed |= (seed & (1 << 0)) << 18;
            //uniqueSeed |= (seed & (1 << 1)) << 21;
            //uniqueSeed |= (seed & (1 << 2)) << 10;
            //uniqueSeed |= (seed & (1 << 3)) << 18;
            //uniqueSeed |= (seed & (1 << 4)) >> 4;
            //uniqueSeed |= (seed & (1 << 5)) << 1;
            //uniqueSeed |= (seed & (1 << 6)) << 4;
            //uniqueSeed |= (seed & (1 << 7)) << 2;
            //uniqueSeed |= (seed & (1 << 8)) >> 5;
            //uniqueSeed |= (seed & (1 << 9)) << 5;
            //uniqueSeed |= (seed & (1 << 10)) << 10;
            //uniqueSeed |= (seed & (1 << 11)) >> 3;
            //uniqueSeed |= (seed & (1 << 12)) >> 8;
            //uniqueSeed |= (seed & (1 << 13)) << 2;
            //uniqueSeed |= (seed & (1 << 14)) >> 12;
            //uniqueSeed |= (seed & (1 << 15)) >> 4;
            //uniqueSeed |= (seed & (1 << 16)) << 3;
            //uniqueSeed |= (seed & (1 << 17)) << 6;
            //uniqueSeed |= (seed & (1 << 18)) >> 1;
            //uniqueSeed |= (seed & (1 << 19)) >> 18;
            //if (itemShuffle > 0)
            //{
            //    uniqueSeed |= 1 << 7;
            //    if (mistDamage) uniqueSeed |= 1 << 13;
            //    if (startWithWheel) uniqueSeed |= 1 << 16;
            //    if (shuffleReliquaries) uniqueSeed |= 1 << 5;
            //}
            uniqueSeed = seed; // First 27 bits
            //uniqueSeed |= (long)((config.LogicDifficulty & 1) << 27);
            //uniqueSeed |= (long)((config.LogicDifficulty & 2) >> 1 << 28);
            //uniqueSeed |= (long)((config.StartingLocation & 1) << 29);
            //uniqueSeed |= (long)((config.StartingLocation & 2) >> 1 << 30);
            //uniqueSeed |= (long)((config.StartingLocation & 4) >> 2 << 31);
            //uniqueSeed |= (long)((config.StartingLocation & 8) >> 3 << 32);

            if ((config.LogicDifficulty & 1) > 0) uniqueSeed |= 1 << 27;
            if ((config.LogicDifficulty & 2) > 0) uniqueSeed |= 1 << 28;
            if ((config.StartingLocation & 1) > 0) uniqueSeed |= 1 << 29;
            if ((config.StartingLocation & 2) > 0) uniqueSeed |= 1 << 30;
            if ((config.StartingLocation & 4) > 0) uniqueSeed |= 1 << 31;
            if ((config.StartingLocation & 8) > 0) uniqueSeed |= 1 << 32;

            if (config.ShuffleReliquaries) uniqueSeed |= 1 << 33;
            if (config.ShuffleDash) uniqueSeed |= 1 << 34;
            if (config.ShuffleWallClimb) uniqueSeed |= 1 << 35;
            if (config.ShuffleBootsOfPleading) uniqueSeed |= 1 << 36;
            if (config.ShufflePurifiedHand) uniqueSeed |= 1 << 37;

            if (config.ShuffleSwordSkills) uniqueSeed |= 1 << 38;
            if (config.ShuffleThorns) uniqueSeed |= 1 << 39;
            if (config.JunkLongQuests) uniqueSeed |= 1 << 40;
            if (config.StartWithWheel) uniqueSeed |= 1 << 41;

            //uniqueSeed |= (long)((config.BossShuffleType & 1) << 42);
            //uniqueSeed |= (long)((config.BossShuffleType & 2) >> 1 << 43);
            //uniqueSeed |= (long)((config.DoorShuffleType & 1) << 44);
            //uniqueSeed |= (long)((config.DoorShuffleType & 2) >> 1 << 45);

            if ((config.BossShuffleType & 1) > 0) uniqueSeed |= 1 << 42;
            if ((config.BossShuffleType & 2) > 0) uniqueSeed |= 1 << 43;
            if ((config.DoorShuffleType & 1) > 0) uniqueSeed |= 1 << 44;
            if ((config.DoorShuffleType & 2) > 0) uniqueSeed |= 1 << 45;

            LogWarning(System.Convert.ToString(uniqueSeed, 2).PadLeft(46, '0'));
            return uniqueSeed;

            long GetMask(byte digit)
            {
                return ((long)1) << digit;
            }
        }

        public StartingLocation StartingDoor
        {
            get
            {
                if (gameConfig.StartingLocation < 0 || gameConfig.StartingLocation >= startingLocations.Length)
                {
                    LogError(gameConfig.StartingLocation + " is not a valid starting location!");
                    return startingLocations[0];
                }
                return startingLocations[gameConfig.StartingLocation];
            }
        }
        private StartingLocation[] startingLocations = new StartingLocation[]
        {
            new StartingLocation("D17Z01S01", "D17Z01S01[E]", new Vector3(-988, 20, 0), true),
            //new StartingLocation("D01Z04S01", "D01Z04S01[W]", new Vector3(-121, -27, 0), true),
            new StartingLocation("D04Z03S01", "D04Z03S01[W]", new Vector3(353, 19, 0), false),
            new StartingLocation("D05Z01S03", "D05Z01S03[W]", new Vector3(318, -4, 0), false),
        };
    }
}
