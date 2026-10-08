# Test deletion evidence

Baseline: `131046880c0653d79baa7280676883adb60af872`. Each location refers to the baseline before removal.

Production remains unchanged. Zero-caller wrappers are retained for a separate production cleanup. Coverage supports the behavior review; it does not establish assertion equivalence.

## Stats

Caller context: scripts/stats, Weapon, BattleUnitState; weapon/unit effective-stat and summary callers. Relevant history: `ccf3eaf`.

| Removed baseline test | Surviving proof / obsolete contract | Reason |
| --- | --- | --- |
| `test/StatFoldTest.cs:47` `StatModifierAddAppliesFlatBonus` | StatFoldTest.AddsSum | Single operation replay is weaker than surviving accumulation/final-clamp contract. |
| `test/StatFoldTest.cs:51` `StatModifierMultiplyAppliesMultiplier` | StatFoldTest.MultiplyCompounds | Single operation replay is weaker than surviving accumulation/final-clamp contract. |
| `test/StatFoldTest.cs:55` `StatModifierCapMinPreventsValueBelowFloor` | StatFoldTest.CapMinIsFinalClamp | Single operation replay is weaker than surviving accumulation/final-clamp contract. |
| `test/StatFoldTest.cs:59` `StatModifierCapMaxPreventsValueAboveCeiling` | StatFoldTest.CapMaxIsFinalClamp | Single operation replay is weaker than surviving accumulation/final-clamp contract. |
| `test/StatResolverTest.cs:9` `ResolveNoSources` | EffectiveStatTest.WeaponRangeContributionTable; StatResolverTest.ResolveIgnoresOtherTargets | Same Resolve/TryResolve path: base/slot Add/separate Add+PercentAdd cases survive at equipment boundary. |
| `test/StatResolverTest.cs:13` `ResolveFoldsMatching` | EffectiveStatTest.WeaponRangeContributionTable; StatResolverTest.ResolveIgnoresOtherTargets | Same Resolve/TryResolve path: base/slot Add/separate Add+PercentAdd cases survive at equipment boundary. |
| `test/StatResolverTest.cs:20` `ResolveStacksAcrossSources` | EffectiveStatTest.WeaponRangeContributionTable; StatResolverTest.ResolveIgnoresOtherTargets | Same Resolve/TryResolve path: base/slot Add/separate Add+PercentAdd cases survive at equipment boundary. |
| `test/StatResolverTest.cs:39` `ConcreteStatModsTargetStatsByClass` | EffectiveStatTest.WeaponRangeContributionTable; StatResolverTest.ResolveIgnoresOtherTargets | Same Resolve/TryResolve path: base/slot Add/separate Add+PercentAdd cases survive at equipment boundary. |

Risk: Low: behavior-reviewed survivor; full baseline, filtered omission, final coverage and deletion-safety review required.

Focused filter: `FullyQualifiedName~StatFoldTest|FullyQualifiedName~StatResolverTest`. Run it with `dotnet test fun-project.csproj --no-restore --filter "<filter>"`; final full runs cover every surviving affected class.

## Combat

Caller context: BattleUnitState, BuffHooks, Weapon, ArmorCapability, ArmorRegenSystem, throw capability resolution; spawn/turn-start/attack/turn-end callers. Relevant history: `ccf3eaf, 4357720, a897af3`.

| Removed baseline test | Surviving proof / obsolete contract | Reason |
| --- | --- | --- |
| `test/WeaponSystemTest.cs:11` `MeleeWeaponConstructedFromRangedWeaponData` | ItemRuntimeFactoryTest; WeaponSystemTest.WeaponGenericStatLookupReturnsConcreteStats | Contrived FirearmWeaponData without ammo passed to MeleeWeapon; production factory never constructs this combination. Valid damage/range/absence/identity contracts survive. |
| `test/WeaponSystemTest.cs:80` `ModSlotStartsEmpty` | WeaponSystemTest.ModSlotCanEquipAndUnequip | Transfer initial empty assertion into existing equip/unequip lifecycle. |
| `test/WeaponSystemTest.cs:99` `MultiStatModAddRemoveAndClearManageInternalList` | EffectiveStatTest weapon contribution tables | Collection forwarding wrappers have zero non-test callers. Exported StatMods/StatContributions path survives; obsolete production wrappers left for separate scope. |
| `test/BuffUnitStateTest.cs:47` `ActiveBuffContributesStats` | BuffBattleTest.ActivationWaitsForTurnStart; DuplicateGrantsContributeStatsAndDamage | Same inactive/active aim and count proven through real hooks and hit preview. |
| `test/BuffUnitStateTest.cs:65` `ClampLowersToReducedMax` | BuffBattleTest.HealthClampOnActivation; DuplicateGrantsEvaluateSequentially | Fixture already clamps before explicit operation. Survivor observes spawn clamp before startup can repair it and deactivation does not heal. |
| `test/BuffDamageTest.cs:18` `EmitDamageFoldsExternalMods` | BuffBattleTest.AttackAppliesActiveDamageBuff | No own mods despite claiming ordering after own mods. Same 5-to-8 external modifier behavior survives through real attack; keep ammunition forwarding probe. |
| `test/ArmorRegenTest.cs:44` `EnemyTurnEndDoesNotTickPlayerRegen` | ArmorRegenTest.RegenWaitsOutDelayThenRestores | Wrong-side ticking accelerates restoration and fails following owner checkpoint. Transfer immediate enemy-turn delay assertion. |
| `test/ArmorRegenTest.cs:142` `DotTickSuppressesSameTurnRegen` | StatusEffectBattleTest.DotSuppressesArmorRegen | DoT-before-regen ordering and post-expiry recovery survive; transfer exact delay countdown. |
| `test/ArmorCapabilityTest.cs:54` `TickRegenDecrementsDelayBeforeRestoring` | ArmorRegenTest.RegenWaitsOutDelayThenRestores; RegenEventLandsAfterTurnEnded | Countdown, per-turn restoration, cap and no event at max survive. Transfer exact capped event amount/current armor. |
| `test/ArmorCapabilityTest.cs:72` `TickRegenCapsAtMax` | ArmorRegenTest.RegenWaitsOutDelayThenRestores; RegenEventLandsAfterTurnEnded | Countdown, per-turn restoration, cap and no event at max survive. Transfer exact capped event amount/current armor. |
| `test/BlastResolutionTest.cs:22` `ThrownGrenadeDamagesEnemyWithinBlastRadius` | BlastResolutionTest.UnitOutsideBlastRadiusIsUnaffected | Identical near-enemy setup/damage plus far negative control survives; transfer damage-event identity and capability event. |

Risk: Low: behavior-reviewed survivor; full baseline, filtered omission, final coverage and deletion-safety review required.

Focused filter: `FullyQualifiedName~WeaponSystemTest|FullyQualifiedName~BuffUnitStateTest|FullyQualifiedName~BuffDamageTest|FullyQualifiedName~ArmorRegenTest|FullyQualifiedName~ArmorCapabilityTest|FullyQualifiedName~BlastResolutionTest`. Run it with `dotnet test fun-project.csproj --no-restore --filter "<filter>"`; final full runs cover every surviving affected class.

## Presentation

Caller context: BattleUI/BattleHud, DialogueView, CharacterModel native graph; BattleScene and Geoscape view consumers. Relevant history: `f085088, f50456b, 5453fd9, fdb6979, fbfb08a`.

| Removed baseline test | Surviving proof / obsolete contract | Reason |
| --- | --- | --- |
| `test/BattleHudTest.cs:19` `ConfirmAndCancelButtonsRaiseIntentEvents` | BattleUITest.PushedViewModelsRender; HudIntentsRaiseThroughRoot; PreviewRendersByKind | Same authored HUD through root checks count/text/identity/intents. Transfer initial-hidden and shown-to-hidden assertions; remove copied fixture. |
| `test/BattleHudTest.cs:35` `VerbButtonsRaiseVerbSelected` | BattleUITest.PushedViewModelsRender; HudIntentsRaiseThroughRoot; PreviewRendersByKind | Same authored HUD through root checks count/text/identity/intents. Transfer initial-hidden and shown-to-hidden assertions; remove copied fixture. |
| `test/BattleHudTest.cs:60` `BattleOverBannerShowsPushedText` | BattleUITest.PushedViewModelsRender; HudIntentsRaiseThroughRoot; PreviewRendersByKind | Same authored HUD through root checks count/text/identity/intents. Transfer initial-hidden and shown-to-hidden assertions; remove copied fixture. |
| `test/BattleHudTest.cs:73` `HitChanceLabelShowsAndHides` | BattleUITest.PushedViewModelsRender; HudIntentsRaiseThroughRoot; PreviewRendersByKind | Same authored HUD through root checks count/text/identity/intents. Transfer initial-hidden and shown-to-hidden assertions; remove copied fixture. |
| `test/DialogueViewTest.cs:192` `PanelAndPortraitStayOnscreen` | MapBattleDialogueLayoutTest.DialogueLowerThirdContainsPortraitAndScrollableLongLines | Same bounds plus long content at three real viewport sizes survive. |
| `test/CharacterModelAnimationAssetTest.cs:147` `TargetSelectionHandlesMultiTrackClips` | MultiChannelPresetCarriesNegativeAndPositiveDeltas; real-scene GraphDrivesPhysicalBoneAndFaceTargets | Tests only a private test helper selecting synthetic channels; no production caller. Native physical graph output tests survive. |

Risk: Low: behavior-reviewed survivor; full baseline, filtered omission, final coverage and deletion-safety review required.

Focused filter: `FullyQualifiedName~DialogueViewTest|FullyQualifiedName~CharacterModelAnimationAssetTest`. Run it with `dotnet test fun-project.csproj --no-restore --filter "<filter>"`; final full runs cover every surviving affected class.

## Strategic

Caller context: GameState, GeoscapeSession, EngineeringState, ResearchState, Armory, authored HUD/view/map consumers. Relevant history: `a897af3, 4ee9c22, f085088, 1310468, 5311f24, ce1347a`.

| Removed baseline test | Surviving proof / obsolete contract | Reason |
| --- | --- | --- |
| `test/CampaignStartDataTest.cs:8` `HoldsAuthoredFields` | GameStateTest.RosterIsStamped; MapBinds; CurrencyAuthoringTest | Assigned auto-property identity copier; real construction/resource consumption survives. |
| `test/GameStateTest.cs:149` `TicksFromDaysCoversWholeDays` | ResearchTest.CapturedDurationSchedulesFromSubmissionTick; ProjectCatalogTest; condition deadlines | Day conversion including zero/one/uint.MaxValue survives in consumers. |
| `test/EngineeringTest.cs:38` `CatalogItemsCanBeManufacturedFromCampaignStart` | UnlimitedProductionCreatesSupplyOnce; ScarceProductionAddsOnePerCompletedJob; OptionsPreserveAuthoredOrderWhileBusyAndReadCurrentArmorySnapshots | Initial registration and both stock policies replayed by stronger complete lifecycle scenarios. |
| `test/EngineeringTest.cs:310` `OneDayJobCompletesAt1440AfterScheduledEventsAndExpiry` | ResearchTest.BothJobsCompleteAfterScheduleAndExpiryInDefinedOrder; FirstManufactureCreatesShelfOnlyAtCompletion | Complete phase order and 1439/1440 stock boundary survive. |
| `test/ManufacturingUnlockTest.cs:46` `RegistrationChangesUnknownToBusyThenExistingSupplyFailure` | EngineeringTest failure precedence; ResearchTest.ResearchUnlocksManufacturingBeforeBroadcast | Failure precedence and real research registration-before-broadcast survive; keep registry order/idempotence owner. |
| `test/GeoscapeClockTest.cs:56` `CurrentTimeAdvancesOneMinutePerTick` | FasterSpeedsTickMore; ElapsedTimeControlsDayCounter | Never reads CurrentTime; actual tick/day assertions already owned elsewhere. |
| `test/GeoscapeClockTest.cs:40` `PausedSpeedFreezesAdvancement` | EngineeringTest.PausedAdvancePreservesJobWithoutAccumulatingProgress | Initial pause plus run/pause/resume and job/events survive. |
| `test/ProjectCatalogTest.cs:25` `EmptyCatalogRemainsCompatible` | EngineeringViewTest.EmptyCatalogExplainsNoOptionsWithoutSelectableRows | Actual empty catalog/idle owner boundary and disabled action survive. |
| `test/ArmoryTest.cs:114` `UnknownEntries` | FirstDeliveryCreatesShelfUsingCapturedStockPolicy; DepositRejectsMissingShelf | Missing withdrawal and deposit failure before touching equipped mods survive. |
| `test/GeoscapeScheduleTest.cs:34` `MultipleEntriesSameTickAllFire` | TacticalEventRetainsBattleSeed; clustered map marker tests | Two same-tick events and many distinct selectable event identities survive. |
| `test/GeoscapeScheduleTest.cs:105` `TargetRegionNameResolves` | GeoscapeMapControlTest.MarkerUsesRegionAnchorAndRoutesItsLifecycle | Real targeted marker placement, identity, click and removal consume resolved target. |
| `test/GeoscapeViewManagerTest.cs:171` `RootWithoutBackButtonIsValid` | All BuildManager stack lifecycle tests | Stronger scenarios use root FakeView with no BackButton; duplicate smoke probe. |
| `test/GeoscapeHudTest.cs:97` `AddedOrdinaryButtonBindsAnotherViewWithoutHudChanges` | BoundButtonsForwardFreshViews | Test supplies the connection it asserts; authored button bindings/freshness remain. Remove private ProbeView. |
| `test/GeoscapeProjectsIntegrationTest.cs:310` `RejectedManufacturingRequestRestoresCampaignSnapshotThenShowsFailure` | GeoscapeProjectsIntegrationTest.StaleProjectSelectionRechecksAffordability and real failure UI | Injects foreign wallet/catalog into previously bound session; no production flow creates this mismatch. |
| `test/CombatantConditionsTest.cs:451` `AuthoredEditsAfterConstructionFlowThroughBoundResources` | AuthoredRulesCustomizeNamesDurationsAndMods; health-floor/composition tests | Deliberate post-construction mutation violates immutable-by-convention contract and pins exact eight-mod representation. |
| `test/RegionButtonTest.cs:122` `CirclePolygonHitsWithinRadius` | GeoscapeMapControlTest.MarkerUsesRegionAnchorAndRoutesItsLifecycle; real clicks; FillPolygonDefinesTheHitArea | Copies presumed circle asset into test-generated polygon; generic and actual authored shape contracts survive. Remove private CirclePolygon. |
| `test/RegionButtonTest.cs:146` `MarkerAnchorUsesTheAuthoredAnchorWhenPresent` | GeoscapeMapControlTest.MarkerUsesRegionAnchorAndRoutesItsLifecycle; MarkerAnchorTracksFillEdits | Both explicit anchor and centroid exact placements survive through real map lifecycle; transform edits retained. |

Risk: Low: behavior-reviewed survivor; full baseline, filtered omission, final coverage and deletion-safety review required.

Focused filter: `FullyQualifiedName~CampaignStartDataTest|FullyQualifiedName~GameStateTest|FullyQualifiedName~EngineeringTest|FullyQualifiedName~ManufacturingUnlockTest|FullyQualifiedName~GeoscapeClockTest|FullyQualifiedName~ProjectCatalogTest|FullyQualifiedName~ArmoryTest|FullyQualifiedName~GeoscapeScheduleTest|FullyQualifiedName~GeoscapeViewManagerTest|FullyQualifiedName~GeoscapeHudTest|FullyQualifiedName~GeoscapeProjectsIntegrationTest|FullyQualifiedName~CombatantConditionsTest|FullyQualifiedName~RegionButtonTest`. Run it with `dotnet test fun-project.csproj --no-restore --filter "<filter>"`; final full runs cover every surviving affected class.

## Battle

Caller context: BattleSession/TurnScheduler, BattleActionExecutor/BattleHookRegistry, BattleFactory/Resolver, objective routing; BattleScene and BattleUiController submission/query callers. Relevant history: `22759d53, 7fa05be3, 70bed31, 687cac2, 4357720`.

| Removed baseline test | Surviving proof / obsolete contract | Reason |
| --- | --- | --- |
| `test/BattleActionExecutorTest.cs:473` `ExecutorResolvesQueuedActionsInFifoOrder` | SubmitReportsMovementAndInterrupts; turn owner tests | Two sequential Submit calls never exercise a simultaneous queue/FIFO order. |
| `test/BattleActionExecutorTest.cs:489` `ExecutorSubmitReturnsOneResultPerSubmittedAction` | SubmitReportsMovementAndInterrupts; turn-transition and throwable consumption owners | Counts a locally authored three-element array; cannot detect executor result-count regression. |
| `test/BattleActionExecutorTest.cs:195` `ExecutorEvaluatesMatchingHooksDeterministically` | BattleHookTest.PriorityThenRegistrationOrderHoldsAcrossDifferentEventKeys | Stronger real executor hook order across key buckets survives. |
| `test/BattleActionExecutorTest.cs:213` `ExecutorOnlyEvaluatesRegisteredEventKeyBucket` | BattleHookTest.HookReceivesOnlyItsConcreteEvent | Unconditional recording hook checks matching/nonmatching routing without self-filtering. |
| `test/BattleActionExecutorTest.cs:246` `ExecutorConsumesOneShotHooksBeforeLaterActions` | BattleHookTest.OneShotRetiresAcrossRegisteredKeys | Stronger cross-key retirement survives; delete private OneShotRecordingHook. |
| `test/BattleSessionTest.cs:24` `BoardDimensionsFollowVector3IAxisOrder` | BattleBoardStateTest.ValidatePoint | Asymmetric dimensions and in/out-of-bounds raw proofs preserve axis order. |
| `test/BattleSessionTest.cs:62` `StartupActivatesFirstParticipatingFaction` | BattleFactoryTest.StartHappyPath; BattleStartupLifecycleTest; TurnSchedulerTest.ConstructionActivatesFirstConsciousSide | Real startup/first turn/AP ordering and scheduler ownership remain. |
| `test/BattleSessionTest.cs:279` `AddingAUnitMidBattleReconcilesTheFactionQueue` | AddingAFactionMidRoundDelaysTheGlobalTurnCounterUntilItActs | Same new faction setup plus full A/B/C round preserved; transfer immediate active-side assertion. |
| `test/BattleSessionTest.cs:447` `SchedulerIgnoresDisabledSpawnCandidates` | TurnSchedulerTest construction skip-dead/KO; integrated conscious/immobilized availability | Inactive disabled unit can be enqueued while IsUnitAvailable remains false; assertion cannot detect claimed guard. Production only adds fresh spawned units. |
| `test/BattleSessionTest.cs:565` `EndUnitActivationAdvancesTheTurnWhenNoActiveUnitsRemain` | PassingTheLastAvailableUnitAdvancesTheGlobalTurnCounter | Actual Pass action covers both sides and round rollover. |
| `test/BattleSessionTest.cs:621` `SessionDefaultsToTheStandardHitChanceCalculator` | BattleCombatQueriesTest.HitChancePreviewMatchesResolution | Pins concrete type; actual default literal 65-40=25 preview/attack calculation survives. |
| `test/BattleFactoryTest.cs:450` `StartTypePlayerDeployment` | BattleSetupResolverTest.DeploymentPreservesIdentityAndCapturesModifiersAtSpawn | Same factory overload plus faction/combatant/weapon/armor/mod identity survives. |
| `test/BattleFactoryTest.cs:468` `EmptyMapPoolFails` | BattleSetupResolverTest.EmptyPoolFailsWithEmptyMapPool; factory type-start failure cases | Trivial Resolve.Bind(Start) wrapper replay; typed failure forwarding independently retained. |
| `test/ObjectiveTest.cs:68` `SurviveUntilTurnPassesAtTargetTurn` | ObjectiveBattleTest.SurviveUntilTurnObjectiveWinsAtTargetTurn; ObjectiveSystemBattleTest.QueuedFollowUpRoutesFromNextEvent | Real authored turn1/2 boundary and ongoing-before-threshold/later pass survive. |
| `test/ObjectiveBattleTest.cs:102` `FailedObjectiveQueuesFollowUpWithInstanceBoundOutcome` | ObjectiveSystemBattleTest.QueuedFollowUpRoutesFromNextEvent | Never reaches named target outcome; transfer count and Failed/Ongoing assertions to actual target-reaching survivor. |
| `test/ObjectiveBattleTest.cs:176` `QueuedFollowUpCanWinOnItsOwnLaterEvent` | ObjectiveSystemBattleTest.QueuedFollowUpRoutesFromNextEvent; QueuedFollowUpObservesNextEventInSameBatch | Fake objective replay of OnFail/later-event/end directive; real SurviveUntilTurn with nondefault authored Defeat survives. |
| `test/BattleVisibilityTest.cs:400` `OwnUnitsRemainKnownToTheirFactionWithoutDirectLineOfSight` | FactionVisibilityPartitionsAlliesAndHostiles | Observer LOS and all/allied/hostile membership survive; transfer separate same-side IsUnitVisibleToFaction assertion. |

Risk: Low: behavior-reviewed survivor; full baseline, filtered omission, final coverage and deletion-safety review required.

Focused filter: `FullyQualifiedName~BattleActionExecutorTest|FullyQualifiedName~BattleSessionTest|FullyQualifiedName~BattleFactoryTest|FullyQualifiedName~ObjectiveTest|FullyQualifiedName~ObjectiveBattleTest|FullyQualifiedName~BattleVisibilityTest`. Run it with `dotnet test fun-project.csproj --no-restore --filter "<filter>"`; final full runs cover every surviving affected class.

## Parameter-row consolidation

Eight additional discovered cases removed from six methods:

| Method | Removed variation | Preserved behavior |
| --- | --- | --- |
| BattleRuntimeTest.RuntimeForwardsQueriesActionsAndEvents | Arbitrary z coordinate | Real query/action/event forwarding with nonzero cell coordinates |
| BattleCombatQueriesTest.HitChancePreviewMatchesResolution | Preview-only false row | Seeded preview and actual resolution equality in one complete row |
| GeoscapeSquadNavigationTest.EngageProducesConfiguredSquadWithoutResolving | Arbitrary title | Authored deployment identity, configured roster, engagement without premature resolution |
| GeoscapeSquadNavigationTest.PreparationRoundTrip | Preparation-only early return | Both complete preparation/engage/reengage lifecycle scenarios retain initial assertions |
| CaptivityViewTest.InterrogationPreparationSelectsDistinctRosterWithoutCampaignEffects | Single-unit row | Explicit one-selection identity/count before selecting distinct second unit; confirm identity and ownership retained |
| EngineeringViewTest.FailureExplainsRejectionAndNextPresentClearsIt | Three synthetically supplied failure enum rows | One formatting/clear lifecycle, plus real stale-affordability rejection integration elsewhere |

## Retained false positives

Keep native Godot graph, viewport/input, cache teardown, export/authoring, and resource round-trip tests even where managed coverage overlaps. Keep distinct payment atomicity, identity, replacement, overflow, boundary, objective outcome, full/incremental visibility, movement-observation and knockout regressions. Shared helper tests guard independent fixture contracts. No assertion is weakened to make tests pass.
