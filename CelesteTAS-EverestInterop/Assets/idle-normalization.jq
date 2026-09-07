# Derives the embedded idle-normalization fixture from a full savestate capture (idle-normalization.base.json).
# Keeps only what a scene-less normalize-`load` must restore deterministically:
#   - hero pose snapshots (Component/Fsm/AudioTable)
#   - RandomState + GameTime/GameFrameCount (pinned so RNG and the deterministic clock are prior-independent).
#     RandomState is load-bearing: `load` does Random.InitState(0) up front, but the scene transition then churns RNG a
#     prior-dependent amount; NormalizeToIdle's fixture apply re-pins RandomState *after* the churn. Dropping it here
#     reintroduces the prior-dependent (greymoor-class) RNG divergence.
#   - a PlayerData SUBSET: only the free-running world-timer fields (FisherWalker*), so `load` canonicalizes
#     those without touching the player's abilities/progress
#   - FixedUpdateCycle forced to 0
# Drops Scene (the `load` command provides it) and SceneData (scene-specific).
# Keeps only Hero_Hornet(Clone) component/FSM snapshots: a capture taken anywhere but a bare spot also grabs nearby
# scene objects (enemies, chests, camera, vines, silkflies, reminders, …), which are scene-specific — same class as
# SceneData and must be dropped so the normalization is hero-only and prior-independent.
# Also strips position from the hero pose (Transform.localPosition / Rigidbody2D.position): the `load` command sets
# the final position itself, so leaving the capture's position in would teleport the hero to that stale spot first.
# Regenerate:  jq -f idle-normalization.jq idle-normalization.base.json > idle-normalization.json
{
  ComponentSnapshots: (.ComponentSnapshots
    | map(select(.Path | startswith("Hero_Hornet(Clone)")))
    | map(
        if (.Path | test("@Transform")) then .Data |= del(.localPosition)
        elif (.Path | test("@Rigidbody2D")) then .Data |= del(.position)
        else . end
      )),
  FsmSnapshots: (.FsmSnapshots | map(select(.Path | startswith("Hero_Hornet(Clone)")))),
  AudioTableSnapshots,
  RandomState,
  GameTime,
  GameFrameCount,
  PlayerData: {
    FisherWalkerTimer: .PlayerData.FisherWalkerTimer,
    FisherWalkerIdleTimeLeft: .PlayerData.FisherWalkerIdleTimeLeft,
    FisherWalkerDirection: .PlayerData.FisherWalkerDirection
  },
  FixedUpdateCycle: 0
}
