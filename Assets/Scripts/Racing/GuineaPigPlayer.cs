// PRD-001
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ybsw.Racing
{
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class GuineaPigPlayer : NetworkBehaviour
    {
        const float MovementSpeed = 8f;
        const float TurnSpeedInDegrees = 120f;

        static readonly Color[] PlayerTints =
        {
            new Color(1f, 0.82f, 0.82f),
            new Color(0.72f, 0.88f, 1f),
            new Color(0.78f, 1f, 0.76f),
            new Color(1f, 0.92f, 0.62f)
        };

        readonly NetworkVariable<Vector2> movementInput = new NetworkVariable<Vector2>(
            Vector2.zero,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<bool> isMoving = new NetworkVariable<bool>();
        readonly NetworkVariable<int> finishPlace = new NetworkVariable<int>();
        readonly NetworkVariable<double> finishTimeInSeconds = new NetworkVariable<double>();

        Rigidbody cachedRigidbody;
        Animator cachedAnimator;
        Renderer[] cachedRenderers;
        MaterialPropertyBlock tintPropertyBlock;

        public int FinishPlace => finishPlace.Value;
        public double FinishTimeInSeconds => finishTimeInSeconds.Value;

        void Awake()
        {
            cachedRigidbody = GetComponent<Rigidbody>();
            cachedAnimator = GetComponentInChildren<Animator>(true);
            cachedRenderers = GetComponentsInChildren<Renderer>(true);
            tintPropertyBlock = new MaterialPropertyBlock();
        }

        public override void OnNetworkSpawn()
        {
            cachedRigidbody.isKinematic = !IsServer;
            isMoving.OnValueChanged += HandleMovingChanged;
            ApplyAnimationState(isMoving.Value);
            ApplyPlayerTint();

            if (IsServer && RaceSession.Instance != null)
            {
                RaceSession.Instance.RegisterPlayer(this);
            }

            if (IsOwner)
            {
                RaceCamera raceCamera = FindAnyObjectByType<RaceCamera>();
                if (raceCamera != null)
                {
                    raceCamera.SetTarget(transform);
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            isMoving.OnValueChanged -= HandleMovingChanged;

            if (IsServer && RaceSession.Instance != null)
            {
                RaceSession.Instance.UnregisterPlayer(this);
            }
        }

        public void ResetOnServer(Vector3 position, Quaternion rotation)
        {
            if (!IsServer)
            {
                return;
            }

            finishPlace.Value = 0;
            finishTimeInSeconds.Value = 0d;
            isMoving.Value = false;
            cachedRigidbody.linearVelocity = Vector3.zero;
            cachedRigidbody.angularVelocity = Vector3.zero;
            cachedRigidbody.position = position;
            cachedRigidbody.rotation = rotation;
        }

        public void FinishOnServer(int place, double elapsedTimeInSeconds)
        {
            if (!IsServer)
            {
                return;
            }

            finishPlace.Value = place;
            finishTimeInSeconds.Value = elapsedTimeInSeconds;
            isMoving.Value = false;
            Vector3 velocity = cachedRigidbody.linearVelocity;
            cachedRigidbody.linearVelocity = new Vector3(0f, velocity.y, 0f);
        }

        void Update()
        {
            if (!IsOwner)
            {
                return;
            }

            Vector2 input = ReadMovementInput();
            if (movementInput.Value != input)
            {
                movementInput.Value = input;
            }
        }

        void FixedUpdate()
        {
            if (!IsServer)
            {
                return;
            }

            RaceSession session = RaceSession.Instance;
            RacePhase phase = session != null ? session.Phase : RacePhase.Waiting;
            Vector2 input = RaceInputUtility.FilterInputForPhase(
                movementInput.Value,
                phase,
                finishPlace.Value > 0);

            float rotationDelta = input.x * TurnSpeedInDegrees * Time.fixedDeltaTime;
            cachedRigidbody.MoveRotation(cachedRigidbody.rotation * Quaternion.Euler(0f, rotationDelta, 0f));

            Vector3 planarVelocity = transform.forward * (input.y * MovementSpeed);
            Vector3 currentVelocity = cachedRigidbody.linearVelocity;
            cachedRigidbody.linearVelocity = new Vector3(planarVelocity.x, currentVelocity.y, planarVelocity.z);

            bool movingNow = Mathf.Abs(input.y) > 0.01f;
            if (isMoving.Value != movingNow)
            {
                isMoving.Value = movingNow;
            }
        }

        Vector2 ReadMovementInput()
        {
            RaceSession session = RaceSession.Instance;
            if (session == null
                || (session.Phase != RacePhase.Countdown && session.Phase != RacePhase.Racing)
                || finishPlace.Value > 0)
            {
                return Vector2.zero;
            }

            float horizontal = 0f;
            float vertical = 0f;
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null && (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed))
            {
                horizontal -= 1f;
            }

            if (keyboard != null && (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed))
            {
                horizontal += 1f;
            }

            if (keyboard != null && (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed))
            {
                vertical += 1f;
            }

            if (keyboard != null && (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed))
            {
                vertical -= 1f;
            }

            Vector2 keyboardInput = Vector2.ClampMagnitude(new Vector2(horizontal, vertical), 1f);
            return RaceInputUtility.ChooseMovementInput(keyboardInput, RaceTouchControls.CurrentInput);
        }

        void HandleMovingChanged(bool previousValue, bool currentValue)
        {
            ApplyAnimationState(currentValue);
        }

        void ApplyAnimationState(bool moving)
        {
            if (cachedAnimator != null)
            {
                cachedAnimator.speed = moving ? 1f : 0f;
            }
        }

        void ApplyPlayerTint()
        {
            Color tint = PlayerTints[OwnerClientId % (ulong)PlayerTints.Length];
            tintPropertyBlock.SetColor("_BaseColor", tint);

            for (int index = 0; index < cachedRenderers.Length; index++)
            {
                Renderer targetRenderer = cachedRenderers[index];
                if (targetRenderer != null)
                {
                    targetRenderer.SetPropertyBlock(tintPropertyBlock);
                }
            }
        }
    }
}
