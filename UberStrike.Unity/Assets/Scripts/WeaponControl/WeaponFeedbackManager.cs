using System;
using System.Collections.Generic;
using UnityEngine;

public class WeaponFeedbackManager : MonoSingleton<WeaponFeedbackManager>
{
    void Awake()
    {
        _bobManager = new WeaponBobManager();
    }

    void OnEnable()
    {
        _dip.Reset();
        _fire.Reset();

        CurrentWeaponMode = WeaponMode.PutDown;
    }

    void Update()
    {
        if (_putDownWeaponState != null) _putDownWeaponState.Update();
        if (_pickupWeaponState != null) _pickupWeaponState.Update();
    }

    WeaponBobManager _bobManager;

    private class WeaponBobManager
    {
        public WeaponBobManager()
        {
            _bobData = new Dictionary<BobMode, BobData>();

            //initialize bobdata for all possible states
            foreach (BobMode b in Enum.GetValues(typeof(BobMode)))
            {
                switch (b)
                {
                    case BobMode.Walk: _bobData[b] = new BobData(0.5f, 3, 6); break;
                    case BobMode.Run: _bobData[b] = new BobData(1, 3, 8); break;
                    case BobMode.Crouch: _bobData[b] = new BobData(0.5f, 3, 12); break;
                    default: _bobData[b] = new BobData(0.0f, 0.0f, 0.0f); break;
                }
            }

            _data = _bobData[BobMode.Idle];
        }

        public BobData Data
        {
            get { return _data; }
        }

        public BobMode Mode
        {
            get { return _bobMode; }
            set
            {
                if (_bobMode != value)
                {
                    _bobMode = value;

                    _data = _bobData[value];
                }
            }
        }

        private readonly Dictionary<BobMode, BobData> _bobData;

        private BobMode _bobMode;

        private BobData _data;

        public struct BobData
        {
            private float _xAmplitude;
            private float _yAmplitude;
            private float _frequency;

            public float XAmplitude { get { return _xAmplitude; } }
            public float YAmplitude { get { return _yAmplitude; } }
            public float Frequency { get { return _frequency; } }

            public BobData(float xamp, float yamp, float freq)
            {
                _xAmplitude = xamp;
                _yAmplitude = yamp;
                _frequency = freq;
            }
        }
    }

    private Quaternion CalculateBobDip()
    {
        if (_dip.time <= _dip.Duration)
        {
            _dip.HandleFeedback();
        }
        else if (_needLerp)
        {
            _angleX = Mathf.Lerp(_angleX, 0, Time.deltaTime * 9);
            _angleY = Mathf.Lerp(_angleY, 0, Time.deltaTime * 9);

            if (_angleX < 0.01f && _angleY < 0.01f)
            {
                _time = 0;
                _needLerp = false;
            }
        }
        else
        {
            float baseWave = Mathf.Sin(_bobManager.Data.Frequency * _time);

            _angleX = Mathf.Abs(_bobManager.Data.XAmplitude * baseWave);
            _angleY = _bobManager.Data.YAmplitude * baseWave * _sign;

            _time += Time.deltaTime;
        }

        return Quaternion.Euler(_angleX, _angleY, 0);
    }

    public void SetBobMode(BobMode mode)
    {
        if (_bobManager.Mode != mode)
        {
            _bobManager.Mode = mode;

            if (mode == BobMode.Run)
            {
                _needLerp = false;
                _sign = InputManager.Instance.IsDown(GameInputKey.Right) ? -1 : 1;
                _time = Mathf.Asin(_angleX / _bobManager.Data.XAmplitude) / _bobManager.Data.Frequency;
            }
            else
            {
                _needLerp = true;
            }
        }
    }

    public void LandingDip()
    {
        // Do not dip the weapon when shooting
        if (_fire.time > 0 && _fire.time < _fire.Duration) return;

        if (CurrentWeaponMode != WeaponMode.PutDown)
        {
            _dip.time = 0;
            _dip.angle = WeaponDip.angle;
            _dip.noise = WeaponDip.noise;
            _dip.strength = WeaponDip.strength;
            _dip.timeToPeak = WeaponDip.timeToPeak;
            _dip.timeToEnd = WeaponDip.timeToEnd;
            _dip.direction = Vector3.down;
            _dip.rotationAxis = Vector3.right;
        }
    }

    public void Fire()
    {
        if (CurrentWeaponMode != WeaponMode.PutDown)
        {
            _fire.noise = WeaponFire.noise;
            _fire.strength = WeaponFire.strength;
            _fire.timeToPeak = WeaponFire.timeToPeak;
            _fire.timeToEnd = WeaponFire.timeToEnd;
            _fire.direction = Vector3.back;
            _fire.rotationAxis = Vector3.left;
            _fire.recoilTime = WeaponFire.recoilTime;

            // clear dip state if shoot
            if (_dip.time < _dip.Duration)
            {
                _dip.Reset();
            }

            // calculate the time of the fire
            // if the time passes the short recoiling period, replay animation
            if (_fire.time > _fire.recoilTime && _fire.time < _fire.Duration)
            {
                _fire.time = WeaponFire.timeToPeak / 3f;
                _fire.angle = WeaponFire.angle / 3f;
            }
            else if (_fire.time >= _fire.Duration)
            {
                _fire.time = 0;
                _fire.angle = WeaponFire.angle;
            }
        }
    }

    public void PutDown(bool destroy = false)
    {
        //put down current weapon
        if (_pickupWeaponState != null && _pickupWeaponState.IsValid)
        {
            PutDownWeapon(_pickupWeaponState.Weapon, _pickupWeaponState.Decorator, destroy);

            _pickupWeaponState = null;
        }
    }

    public void PickUp(BaseWeaponLogic weapon, BaseWeaponDecorator decorator)
    {
        //put down current weapon
        if (_pickupWeaponState != null && _pickupWeaponState.IsValid)
        {
            //but only if the weapons are actually different
            if (_pickupWeaponState.Weapon != weapon)
            {
                // Hide the outgoing weapon's decorator immediately so it doesn't
                // render on top of the incoming PickUpState pose. Switching guns
                // routes straight here (WeaponController.PickUp), never through
                // PutDown(). Non-melee weapons share the same pivot transform, so
                // without this the prev gun ghosts in roughly the new gun's
                // position for PutDownDuration (the Vlad / Spiteful Stinger
                // scope-after-switch bug). Re-applied after commit 672cf5e4
                // ("Base on HaZard production") reverted this file to HaZard's
                // upstream, which never merged PR #72. Mirrors PR #72 / c6b9cb93.
                BaseWeaponDecorator prevDecorator = _pickupWeaponState.Decorator;
                PutDownWeapon(_pickupWeaponState.Weapon, _pickupWeaponState.Decorator);
                if ((bool)prevDecorator)
                    prevDecorator.IsEnabled = false;
            }
            else
                return;
        }
        else if (_pickupWeaponState == null && _putDownWeaponState != null && _putDownWeaponState.Weapon == weapon)
        {
            _putDownWeaponState.Finish();
        }

        //pick up new weapon
        _pickupWeaponState = new PickUpState(weapon, decorator);

        WeaponFire.recoilTime = WeaponConfigurationHelper.GetRateOfFire(weapon.Config);
        WeaponFire.strength = WeaponConfigurationHelper.GetRecoilMovement(weapon.Config);
        WeaponFire.angle = WeaponConfigurationHelper.GetRecoilKickback(weapon.Config);
    }

    public void BeginIronSight()
    {
        if (!_isIronSight)
        {
            _isIronSight = true;
        }
    }

    public void EndIronSight()
    {
        _isIronSight = false;
    }

    public void ResetIronSight()
    {
        _isIronSight = false;

        if (_pickupWeaponState != null)
            _pickupWeaponState.Reset();

        if (_putDownWeaponState != null)
            _putDownWeaponState.Reset();
    }

    public bool IsIronSighted
    {
        get { return _isIronSight; }
    }

    #region Weapon State

    private WeaponMode _currentWeaponMode;

    public enum WeaponMode
    {
        Primary,
        Second,
        PutDown
    }

    private WeaponState _pickupWeaponState;
    private WeaponState _putDownWeaponState;

    private void PutDownWeapon(BaseWeaponLogic weapon, BaseWeaponDecorator decorator, bool destroy = false)
    {
        if (_putDownWeaponState != null)
            _putDownWeaponState.Finish();

        _putDownWeaponState = new PutDownState(weapon, decorator, destroy);
    }

    public WeaponMode CurrentWeaponMode
    {
        get { return _currentWeaponMode; }
        private set { _currentWeaponMode = value; }
    }

    /// <summary>
    /// 
    /// </summary>
    private abstract class WeaponState
    {
        protected WeaponState(BaseWeaponLogic weapon, BaseWeaponDecorator decorator)
        {
            _time = 0;

            _weapon = weapon;

            _decorator = decorator;

            _isRunning = (_weapon != null);
        }

        public abstract void Update();

        public abstract void Finish();

        public void Reset()
        {
            _pivotOffset = new Vector3(0, 0, 0.2f);
        }

        public Vector3 PivotVector { get { return _pivotOffset + (Instance._isIronSight ? Decorator.IronSightPosition : Decorator.DefaultPosition); } }

        public virtual bool CanTransit(WeaponMode mode)
        {
            return Instance.CurrentWeaponMode != mode;
        }

        #region PROPERTIES
        public bool IsRunning { get { return _isRunning; } }
        public bool IsValid { get { return _weapon != null && _decorator != null; } }
        public BaseWeaponDecorator Decorator { get { return _decorator; } }
        public BaseWeaponLogic Weapon { get { return _weapon; } }
        public Vector3 TargetPosition { get { return _targetPosition; } }
        public Quaternion TargetRotation { get { return _targetRotation; } }
        #endregion

        #region FIELDS
        protected bool _isRunning;
        protected float _time;
        private BaseWeaponLogic _weapon;
        private BaseWeaponDecorator _decorator;
        #endregion

        protected Vector3 _pivotOffset;
        protected float _currentRotation;
        protected float _transitionTime;

        protected Vector3 _targetPosition;
        protected Quaternion _targetRotation;
    }

    private class PickUpState : WeaponState
    {
        public PickUpState(BaseWeaponLogic weapon, BaseWeaponDecorator decorator)
            : base(weapon, decorator)
        {
            _transitionTime = Mathf.Max(Instance.WeaponAnimation.PickUpDuration, weapon.Config.SwitchDelayMilliSeconds / 1000);
            if (decorator.IsMelee)
            {
                _currentRotation = -90;

                if (Decorator)
                {
                    //reset weapon
                    Decorator.CurrentRotation = Quaternion.Euler(0, 0, _currentRotation);
                    Decorator.CurrentPosition = decorator.DefaultPosition;
                    Decorator.IsEnabled = true;
                }
            }
            else
            {
                _currentRotation = Instance.WeaponAnimation.PutDownAngles;
                _pivotOffset = -Instance._pivotPoint.localPosition;

                if (Decorator)
                {
                    //reset weapon
                    Decorator.CurrentRotation = Quaternion.Euler(Instance.WeaponAnimation.PutDownAngles, 0, 0);
                    Decorator.CurrentPosition = Quaternion.AngleAxis(_currentRotation, Vector3.right) * PivotVector;
                    Decorator.IsEnabled = true;
                }
            }

            LevelCamera.Instance.ResetZoom();
        }

        public override void Update()
        {
            if (IsValid)
            {
                if (IsRunning)
                {
                    if (_time <= _transitionTime)
                    {
                        //calculate target position/rotation
                        _currentRotation = Mathf.Lerp(_currentRotation, Instance.WeaponAnimation.PickUpAngles, _time / _transitionTime);

                        //calculate target position/rotation
                        if (Decorator.IsMelee)
                        {
                            _targetPosition = Decorator.DefaultPosition;
                            _targetRotation = Quaternion.Euler(0, 0, _currentRotation);
                        }
                        else
                        {
                            _targetPosition = Quaternion.AngleAxis(_currentRotation, Vector3.right) * PivotVector;
                            _targetRotation = Quaternion.Euler(_currentRotation + Decorator.DefaultAngles.x, Decorator.DefaultAngles.y, Decorator.DefaultAngles.z);
                        }

                        if (!Instance._isIronSight)
                        {
                            Decorator.CurrentPosition = _targetPosition;
                            Decorator.CurrentRotation = _targetRotation;
                        }

                        _time += Time.deltaTime;
                    }

                    //enable the weapon before it reaches final position
                    if (_time > _transitionTime * 0.25f)
                        Weapon.IsWeaponActive = true;

                    if (_time > _transitionTime)
                        Finish();
                }

                if (_time > _transitionTime * 0.25f)
                {
                    if (Instance._isIronSight)
                    {
                        _pivotOffset = Vector3.Lerp(_pivotOffset, Vector2.zero, Time.deltaTime * 20);

                        if (Decorator.CurrentPosition == Decorator.IronSightPosition)
                            Instance._isIronSightPosDone = true;
                        else
                            Instance._isIronSightPosDone = false;
                    }
                    else
                    {
                        _pivotOffset = Vector3.Lerp(_pivotOffset, new Vector3(0, 0, 0.2f), Time.deltaTime * 10);
                    }

                    // fire (continous)
                    if (Instance._fire.time < Instance._fire.Duration)
                    {
                        if (!IsRunning)
                        {
                            if (!Instance._isIronSight && _pivotOffset == new Vector3(0, 0, 0.2f))
                            {
                                Instance._fire.HandleFeedback();
                                Decorator.CurrentPosition = _targetPosition + Instance._fire.PositionOffset;
                                Decorator.CurrentRotation = _targetRotation * Instance._fire.RotationOffset;
                            }
                            else
                            {
                                Decorator.CurrentPosition = PivotVector + Instance._dip.PositionOffset;
                                Decorator.CurrentRotation = _targetRotation * Instance._dip.RotationOffset;
                            }

                            _isFiring = true;
                        }
                    }
                    // bob, dip (continous)
                    else
                    {
                        if (_isFiring)
                        {
                            _isFiring = false;

                            Instance._time = 0;
                            Instance._angleX = 0;
                            Instance._angleY = 0;
                        }

                        Quaternion bobRot = Quaternion.identity;
                        if (Instance._isIronSight && Instance._dip.PositionOffset == Vector3.zero)
                        {
                            bobRot = Quaternion.identity;
                        }
                        else
                        {
                            bobRot = Instance.CalculateBobDip();
                        }

                        // movement imitation
                        // the bob & dip should move around the axis, just like weapon switching
                        if (!Decorator.IsMelee)
                        {
                            Decorator.CurrentPosition = bobRot * PivotVector + Instance._dip.PositionOffset;
                            Decorator.CurrentRotation = _targetRotation * Instance._dip.RotationOffset * bobRot;
                        }
                        else
                        {
                            Decorator.CurrentRotation = _targetRotation * Instance._dip.RotationOffset * bobRot;
                        }
                    }
                }
            }
        }

        public override void Finish()
        {
            //avoid multiple finilizations
            if (_isRunning)
            {
                //stop running in update
                _isRunning = false;

                if (Weapon != null)
                {
                    //finally we activate the weapon
                    Weapon.IsWeaponActive = true;
                    Instance._currentWeaponMode = WeaponMode.Primary;
                }

                if (Decorator.IsMelee)
                {
                    _targetRotation = Quaternion.Euler(0, 0, Instance.WeaponAnimation.PickUpAngles);
                    _targetPosition = Decorator.DefaultPosition;
                }
                else
                {
                    _targetRotation = Quaternion.Euler(Instance.WeaponAnimation.PickUpAngles + Decorator.DefaultAngles.x, Decorator.DefaultAngles.y, Decorator.DefaultAngles.z);
                    _targetPosition = Quaternion.AngleAxis(Instance.WeaponAnimation.PickUpAngles, Vector3.right) * PivotVector;
                }
            }
        }

        public override string ToString()
        {
            return "Pick Up State";
        }

        private bool _isFiring = false;
    }

    /// <summary>
    /// When you want to change weapon, the manager enters this state.
    /// </summary>
    private class PutDownState : WeaponState
    {
        private bool _destroy = false;

        public PutDownState(BaseWeaponLogic weapon, BaseWeaponDecorator decorator, bool destroy = false)
            : base(weapon, decorator)
        {
            _destroy = destroy;
            _currentRotation = decorator.CurrentRotation.eulerAngles.x;

            if (_currentRotation > 300) _currentRotation = 360 - _currentRotation;
            if (!decorator.IsMelee)
            {
                _pivotOffset = -Instance._pivotPoint.localPosition;
            }
            _transitionTime = Instance.WeaponAnimation.PutDownDuration;

            //first of all deactive the weapon
            if (Weapon != null)
            {
                Weapon.IsWeaponActive = false;
            }
        }

        public override void Update()
        {
            if (IsRunning && IsValid)
            {
                if (_time > _transitionTime) return;

                //calculate target position/rotation
                if (Decorator.IsMelee)//EffectType == ImpactEffectType.MeleeDefault)
                {
                    _currentRotation = Mathf.Lerp(_currentRotation, -90, _time / _transitionTime);
                    _targetPosition = Decorator.DefaultPosition;
                    _targetRotation = Quaternion.Euler(0, 0, _currentRotation);
                }
                else
                {
                    _currentRotation = Mathf.Lerp(_currentRotation, Instance.WeaponAnimation.PutDownAngles, _time / _transitionTime);
                    _targetPosition = Quaternion.AngleAxis(_currentRotation, Vector3.right) * PivotVector;
                    _targetRotation = Quaternion.Euler(_currentRotation, 0, 0);
                }

                Decorator.CurrentPosition = _targetPosition;
                Decorator.CurrentRotation = _targetRotation;

                _time += Time.deltaTime;

                //disable the weapon visually after animation is done
                if (_time > _transitionTime)
                    Finish();
            }
        }

        public override void Finish()
        {
            //avoid multiple finilizations
            if (_isRunning)
            {
                //stop running in update
                _isRunning = false;

                if (Decorator)
                {
                    Decorator.IsEnabled = false;

                    Decorator.CurrentPosition = Decorator.DefaultPosition;
                    Decorator.CurrentRotation = _targetRotation;

                    if (_destroy)
                        Destroy(Decorator.gameObject);
                }
            }
        }

        public override string ToString()
        {
            return "Put down";
        }
    }

    #endregion

    #region Weapon Feedback

    [System.Serializable]
    public class FeedbackData
    {
        public float timeToPeak;
        public float timeToEnd;
        public float noise;
        public float angle;
        public float strength;
        public float recoilTime;
    }

    public FeedbackData WeaponDip;
    public FeedbackData WeaponFire;

    protected struct Feedback
    {
        public float time;
        public float noise;
        public float angle;
        public float timeToPeak;
        public float timeToEnd;
        public float strength;
        public float recoilTime;

        public Vector3 direction;
        public Vector3 rotationAxis;

        private float _maxAngle;
        private float _angle;

        private Vector3 _positionOffset;
        private Quaternion _rotationOffset;

        public float DebugAngle
        {
            get { return _angle; }
        }

        public float Duration
        {
            get { return timeToPeak + timeToEnd; }
        }

        /// <summary>
        /// The position result of HandleFeedback
        /// </summary>
        public Vector3 PositionOffset
        {
            get { return _positionOffset; }
        }

        /// <summary>
        /// The rotation result of HandleFeedback
        /// </summary>
        public Quaternion RotationOffset
        {
            get { return _rotationOffset; }
        }

        /// <summary>
        /// Calculate the feedback every frame, then access the results by PositionOffset and RotationOffset
        /// </summary>
        public void HandleFeedback()
        {
            float force = 0;
            float noiseValue = UnityEngine.Random.Range(-noise, noise);

            _maxAngle = Mathf.Lerp(_maxAngle, angle, Time.deltaTime * 10);

            if (time < Duration)
            {
                time += Time.deltaTime;

                if (time < Duration)
                {
                    if (time < timeToPeak)
                    {
                        force = strength * Mathf.Sin(time * Mathf.PI * 0.5f / timeToPeak);
                        noise = Mathf.Lerp(noise, 0, time / timeToPeak);
                        _angle = Mathf.Lerp(0, _maxAngle, Mathf.Pow(time / timeToPeak, 2));
                    }
                    else
                    {
                        float t = (time - timeToPeak) / timeToEnd;

                        force = strength * Mathf.Cos((time - timeToPeak) * Mathf.PI * 0.5f / timeToEnd);
                        _angle = Mathf.Lerp(_maxAngle, 0, t);

                        if (time != 0) noiseValue = 0;
                    }

                    if (WeaponController.Instance.CurrentDecorator)
                    {
                        _positionOffset = force * direction +
                            WeaponController.Instance.CurrentDecorator.transform.right * noiseValue +
                            WeaponController.Instance.CurrentDecorator.transform.up * noiseValue;

                        _rotationOffset = Quaternion.AngleAxis(_angle, rotationAxis);
                    }
                }
                else
                {
                    _angle = 0;
                    _positionOffset = Vector3.zero;
                    _rotationOffset = Quaternion.identity;
                }
            }
            else
            {
                time = 0;
                _angle = 0;
                _positionOffset = Vector3.zero;
                _rotationOffset = Quaternion.identity;
            }
        }

        public void Reset()
        {
            time = 0;
            timeToEnd = 0;
            timeToPeak = -1;

            angle = 0;
            direction = Vector3.zero;

            _angle = 0;
            _positionOffset = Vector3.zero;
            _rotationOffset = Quaternion.identity;
        }
    }

    protected Feedback _fire;
    protected Feedback _dip;

    private bool _needLerp;

    public void SetFireFeedback(FeedbackData data)
    {
        WeaponFire.angle = data.angle;
        WeaponFire.noise = data.noise;
        WeaponFire.strength = data.strength;
        WeaponFire.timeToEnd = data.timeToEnd;
        WeaponFire.timeToPeak = data.timeToPeak;
        WeaponFire.recoilTime = data.recoilTime;
    }

    #endregion

    #region Weapon Animation

    [System.Serializable]
    public class WeaponAnimData
    {
        public float PutDownAngles = 30;
        public float PutDownDuration;

        public float PickUpAngles = 0;
        public float PickUpDuration;
    }

    public WeaponAnimData WeaponAnimation;

    #endregion

    private float _angleY;
    private float _angleX;
    private float _time;
    private float _sign;

    [SerializeField]
    private Transform _pivotPoint;

    private bool _isIronSight;
    private bool _isIronSightPosDone = false;
    public bool _isWeaponInIronSightPosition
    {
        get { return _isIronSight && _isIronSightPosDone; }
    }
}
