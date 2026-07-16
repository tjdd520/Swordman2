using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Swordman2.Combat
{
    public sealed class CombatDirector : MonoBehaviour
    {
        private readonly Queue<string> playerOneAttacks = new();
        private readonly Queue<string> playerTwoAttacks = new();
        private CombatCatalogData catalog;
        private CombatSimulation simulation;
        private CombatSnapshot currentSnapshot;
        private float simulationStep;
        private float accumulator;
        private Vector2 playerOneMove;
        private Vector2 playerTwoMove;
        private CombatAudio combatAudio;

        public CombatCatalogData Catalog => catalog;
        public CombatSimulation Simulation => simulation;
        public CombatSnapshot CurrentSnapshot => currentSnapshot?.Clone(catalog);
        public FighterController PlayerOne { get; private set; }
        public FighterController PlayerTwo { get; private set; }
        public string LastEvent { get; private set; } = "战斗开始";

        public void Initialize(FighterController playerOne, FighterController playerTwo,
            CombatAudio audio, CombatCatalogData combatCatalog)
        {
            PlayerOne = playerOne;
            PlayerTwo = playerTwo;
            combatAudio = audio;
            catalog = combatCatalog;
            simulationStep = 1f / catalog.settings.logicFrameRate;
            playerOne.Opponent = playerTwo;
            playerTwo.Opponent = playerOne;
            Vector3 firstPosition = playerOne.Root.transform.position;
            Vector3 secondPosition = playerTwo.Root.transform.position;
            simulation = new CombatSimulation(catalog,
                firstPosition.x, firstPosition.z, secondPosition.x, secondPosition.z);
            currentSnapshot = simulation.CaptureSnapshot();
            playerOne.Bind(this, currentSnapshot.playerOne);
            playerTwo.Bind(this, currentSnapshot.playerTwo);
        }

        private void Update()
        {
            if (simulation == null || Time.timeScale <= 0f) return;
            ReadInput();
            accumulator = Mathf.Min(accumulator + Time.deltaTime, 0.1f);
            while (accumulator >= simulationStep)
            {
                Simulate();
                accumulator -= simulationStep;
            }
        }

        public void QueueAttack(int playerIndex, string actionId)
        {
            if (catalog?.GetAttack(actionId) == null)
            {
                Debug.LogError($"P{playerIndex} 尝试执行不存在的动作：{actionId}");
                return;
            }
            Queue<string> queue = playerIndex == 1 ? playerOneAttacks : playerTwoAttacks;
            queue.Enqueue(actionId);
        }

        public void Teleport(int playerIndex, Vector3 position)
        {
            simulation?.Teleport(playerIndex, position.x, position.z);
            ApplyCurrentSnapshot(0f);
        }

        public void RefreshFacing()
        {
            simulation?.RecalculateFacing();
            ApplyCurrentSnapshot(0f);
        }

        public void RestoreVitals(int playerIndex)
        {
            simulation?.RestoreVitals(playerIndex);
            ApplyCurrentSnapshot(0f);
        }

        public void SetTemporaryVitals(int playerIndex, float health, float stance)
        {
            simulation?.SetTemporaryVitals(playerIndex, health, stance);
            ApplyCurrentSnapshot(0f);
        }

        public void ApplyAuthoritativeSnapshot(CombatSnapshot snapshot)
        {
            if (simulation == null || snapshot == null) return;
            playerOneAttacks.Clear();
            playerTwoAttacks.Clear();
            simulation.ApplySnapshot(snapshot);
            ApplyCurrentSnapshot(0f);
        }

        private void ReadInput()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                playerOneMove = playerTwoMove = Vector2.zero;
                return;
            }
            playerOneMove = ReadMove(keyboard, catalog.controls.playerOne);
            playerTwoMove = ReadMove(keyboard, catalog.controls.playerTwo);
            ReadAttacks(keyboard, catalog.controls.playerOne, 1);
            ReadAttacks(keyboard, catalog.controls.playerTwo, 2);
        }

        private void Simulate()
        {
            long tick = simulation.Tick + 1;
            string firstAction = playerOneAttacks.Count > 0 ? playerOneAttacks.Dequeue() : null;
            string secondAction = playerTwoAttacks.Count > 0 ? playerTwoAttacks.Dequeue() : null;
            CombatInputCommand first = new CombatInputCommand(tick, 1,
                playerOneMove.x, playerOneMove.y, firstAction);
            CombatInputCommand second = new CombatInputCommand(tick, 2,
                playerTwoMove.x, playerTwoMove.y, secondAction);
            currentSnapshot = simulation.Step(first, second);
            ProcessEvents(simulation.Events);
            LastEvent = currentSnapshot.lastEvent;
            PlayerOne.ApplySnapshot(currentSnapshot.playerOne, simulationStep);
            PlayerTwo.ApplySnapshot(currentSnapshot.playerTwo, simulationStep);
        }

        private void ApplyCurrentSnapshot(float deltaTime)
        {
            if (simulation == null) return;
            currentSnapshot = simulation.CaptureSnapshot();
            LastEvent = currentSnapshot.lastEvent;
            PlayerOne?.ApplySnapshot(currentSnapshot.playerOne, deltaTime);
            PlayerTwo?.ApplySnapshot(currentSnapshot.playerTwo, deltaTime);
        }

        private void ProcessEvents(IReadOnlyList<CombatEvent> combatEvents)
        {
            foreach (CombatEvent combatEvent in combatEvents)
            {
                if (combatEvent.type == CombatEventType.ActionPairPredicted)
                    combatAudio?.PlayActionPair(combatEvent.predictedTimeSeconds);
                else if (combatEvent.type == CombatEventType.NormalHit)
                    combatAudio?.PlayNormalHit();
                if (!string.IsNullOrWhiteSpace(combatEvent.message)) LastEvent = combatEvent.message;
            }
        }

        private static Vector2 ReadMove(Keyboard keyboard, PlayerControls controls)
        {
            float x = (IsPressed(keyboard, controls.moveRight) ? 1f : 0f) -
                      (IsPressed(keyboard, controls.moveLeft) ? 1f : 0f);
            float y = (IsPressed(keyboard, controls.moveUp) ? 1f : 0f) -
                      (IsPressed(keyboard, controls.moveDown) ? 1f : 0f);
            return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
        }

        private void ReadAttacks(Keyboard keyboard, PlayerControls controls, int playerIndex)
        {
            foreach (AttackBinding binding in controls.attacks)
                if (WasPressed(keyboard, binding.key)) QueueAttack(playerIndex, binding.action);
        }

        private static bool IsPressed(Keyboard keyboard, string keyName) =>
            Enum.TryParse(keyName, true, out Key key) && keyboard[key].isPressed;

        private static bool WasPressed(Keyboard keyboard, string keyName) =>
            Enum.TryParse(keyName, true, out Key key) && keyboard[key].wasPressedThisFrame;

        private void OnDestroy()
        {
            PlayerOne?.Dispose();
            PlayerTwo?.Dispose();
        }
    }
}
