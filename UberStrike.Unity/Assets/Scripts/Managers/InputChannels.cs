using System.Collections.Generic;
using Cmune.Realtime.Common;
using Cmune.Realtime.Common.IO;
using UnityEngine;

public interface IInputChannel : IByteArray
{
    InputChannelType ChannelType { get; }
    string Name { get; }
    bool IsChanged { get; }
    float RawValue();
    float Value { get; }

    void Listen();
    void Reset();
}

public class KeyInputChannel : IInputChannel, IByteArray
{
    public KeyInputChannel(byte[] bytes, ref int idx)
    {
        idx = FromBytes(bytes, idx);
        InitKeyName();
    }

    public KeyInputChannel(KeyCode key)
    {
        _key = key;
        InitKeyName();
    }

    private void InitKeyName()
    {
        switch (_key)
        {
            case KeyCode.Alpha0:
                _name = "0";
                break;
            case KeyCode.Alpha1:
                _name = "1";
                break;
            case KeyCode.Alpha2:
                _name = "2";
                break;
            case KeyCode.Alpha3:
                _name = "3";
                break;
            case KeyCode.Alpha4:
                _name = "4";
                break;
            case KeyCode.Alpha5:
                _name = "5";
                break;
            case KeyCode.Alpha6:
                _name = "6";
                break;
            case KeyCode.Alpha7:
                _name = "7";
                break;
            case KeyCode.Alpha8:
                _name = "8";
                break;
            case KeyCode.Alpha9:
                _name = "9";
                break;
            case KeyCode.Keypad0:
                _name = "Keypad 0";
                break;
            case KeyCode.Keypad1:
                _name = "Keypad 1";
                break;
            case KeyCode.Keypad2:
                _name = "Keypad 2";
                break;
            case KeyCode.Keypad3:
                _name = "Keypad 3";
                break;
            case KeyCode.Keypad4:
                _name = "Keypad 4";
                break;
            case KeyCode.Keypad5:
                _name = "Keypad 5";
                break;
            case KeyCode.Keypad6:
                _name = "Keypad 6";
                break;
            case KeyCode.Keypad7:
                _name = "Keypad 7";
                break;
            case KeyCode.Keypad8:
                _name = "Keypad 8";
                break;
            case KeyCode.Keypad9:
                _name = "Keypad 9";
                break;
            case KeyCode.KeypadDivide:
                _name = "Keypad Divide";
                break;
            case KeyCode.KeypadEnter:
                _name = "Keypad Enter";
                break;
            case KeyCode.KeypadEquals:
                _name = "Keypad Equals";
                break;
            case KeyCode.KeypadMinus:
                _name = "Keypad Minus";
                break;
            case KeyCode.KeypadMultiply:
                _name = "Keypad Multiply";
                break;
            case KeyCode.KeypadPeriod:
                _name = "Keypad Period";
                break;
            case KeyCode.KeypadPlus:
                _name = "Keypad Plus";
                break;
            case KeyCode.LeftArrow:
                _name = "Left Arrow";
                break;
            case KeyCode.RightArrow:
                _name = "Right Arrow";
                break;
            case KeyCode.UpArrow:
                _name = "Up Arrow";
                break;
            case KeyCode.DownArrow:
                _name = "Down Arrow";
                break;
            case KeyCode.LeftAlt:
                _name = "Left Alt";
                break;
            case KeyCode.LeftControl:
                _name = "Left Ctrl";
                break;
            case KeyCode.LeftShift:
                _name = "Left Shift";
                break;
            case KeyCode.RightAlt:
                _name = "Right Alt";
                break;
            case KeyCode.RightControl:
                _name = "Right Ctrl";
                break;
            case KeyCode.RightShift:
                _name = "Right Shift";
                break;
            default: _name = _key.ToString(); break;
        }
    }

    public void Listen()
    {
        _wasDown = _isDown;
        _isDown = Input.GetKey(_key);
    }


    public void Reset()
    {
        _wasDown = false;
        _isDown = false;
    }

    public float RawValue()
    {
        return Input.GetKey(_key) ? 1 : 0;
    }

    public override string ToString()
    {
        return _key.ToString();
    }

    public override bool Equals(object obj)
    {
        if (obj is KeyInputChannel)
        {
            KeyInputChannel c = obj as KeyInputChannel;
            if (c.Key == Key)
                return true;
        }

        return false;
    }

    public override int GetHashCode()
    {
        return base.GetHashCode();
    }

    #region FIELDS
    private bool _wasDown = false;
    private bool _isDown = false;
    private KeyCode _key = KeyCode.None;
    private string _name = string.Empty;
    #endregion

    #region PROPOERTIES
    public KeyCode Key
    {
        get { return _key; }
    }
    public string Name
    { get { return _name; } }
    public InputChannelType ChannelType
    {
        get { return InputChannelType.Key; }
    }
    public float Value
    {
        get { return _isDown ? 1 : 0; }
        set { _isDown = _wasDown = value != 0 ? true : false; }
    }
    public bool IsChanged
    {
        get { return _isDown != _wasDown; }
    }
    #endregion

    #region IByteArray Members

    public int FromBytes(byte[] bytes, int idx)
    {
        _key = (KeyCode)DefaultByteConverter.ToInt(bytes, ref idx);
        return idx;
    }

    public byte[] GetBytes()
    {
        List<byte> bytes = new List<byte>(4);
        DefaultByteConverter.FromInt((int)_key, ref bytes);
        return bytes.ToArray();
    }

    #endregion
}

public class MouseInputChannel : IInputChannel, IByteArray
{
    public MouseInputChannel(byte[] bytes, ref int idx)
    {
        idx = FromBytes(bytes, idx);
    }

    public MouseInputChannel(int button)
    {
        _button = button;
    }

    public void Listen()
    {
        _wasDown = _isDown;
        _isDown = Input.GetMouseButton(_button);
    }

    public float RawValue()
    {
        return Input.GetMouseButton(_button) ? 1 : 0;
    }

    public void Reset()
    {
        _wasDown = false;
        _isDown = false;
    }

    public override string ToString()
    {
        return string.Format("Mouse {0}", _button);
    }

    public override bool Equals(object obj)
    {
        if (obj is MouseInputChannel)
        {
            MouseInputChannel m = obj as MouseInputChannel;
            if (m.Button == Button)
                return true;
        }

        return false;
    }

    public override int GetHashCode()
    {
        return base.GetHashCode();
    }

    private string ConvertMouseButtonName(int _button)
    {
        switch (_button)
        {
            case 0:
                return "Left Mousebutton";
            case 1:
                return "Right Mousebutton";
            default:
                return string.Format("Mouse {0}", _button);
        }
    }

    #region FIELDS
    private bool _wasDown = false;
    private bool _isDown = false;
    private int _button = 0;
    #endregion

    #region PROPERTIES
    public int Button
    {
        get { return _button; }
    }
    public string Name
    { get { return ConvertMouseButtonName(_button); } }
    //{ get { return string.Format("Mouse {0}", _button); } }
    public InputChannelType ChannelType
    {
        get { return InputChannelType.Mouse; }
    }
    public float Value
    {
        get { return _isDown ? 1 : 0; }
        set { _isDown = _wasDown = value != 0 ? true : false; }
    }
    public bool IsChanged
    {
        get { return _isDown != _wasDown; }
    }
    #endregion

    #region IByteArray Members

    public int FromBytes(byte[] bytes, int idx)
    {
        _button = DefaultByteConverter.ToInt(bytes, ref idx);
        return idx;
    }

    public byte[] GetBytes()
    {
        List<byte> bytes = new List<byte>(4);
        DefaultByteConverter.FromInt(_button, ref bytes);
        return bytes.ToArray();
    }

    #endregion
}

public class AxisInputChannel : IInputChannel, IByteArray
{
    public AxisInputChannel(byte[] bytes, ref int idx)
    {
        idx = FromBytes(bytes, idx);
    }

    public AxisInputChannel(string axis)
    {
        _axis = axis;
        _axisName = _axis;
    }

    public AxisInputChannel(string axis, float deadRange)
        : this(axis)
    {
        _deadRange = deadRange;
    }

    public AxisInputChannel(string axis, float deadRange, AxisReadingMethod method)
        : this(axis, deadRange)
    {
        _axisReading = method;
        switch (method)
        {
            case AxisReadingMethod.NegativeOnly:
                _axisName += " Up";
                break;
            case AxisReadingMethod.PositiveOnly:
                _axisName += " Down";
                break;
        }
    }

    public void Listen()
    {
        _lastValue = _value;
        _value = RawValue();

        //handle one directional axis
        switch (_axisReading)
        {
            case AxisReadingMethod.NegativeOnly: if (_value > 0) _value = 0; break;
            case AxisReadingMethod.PositiveOnly: if (_value < 0) _value = 0; break;
        }

        //prune in deadzone
        if (Mathf.Abs(_value) < _deadRange)
            _value = 0;
    }

    public void Reset()
    {
        _value = 0;
        _lastValue = 0;
    }

    public float RawValue()
    {
        // Mouse scroll wheel is a DISCRETE input — each wheel click produces
        // a one-frame delta. Input.GetAxis applies axis smoothing (designed
        // for analog sticks), which ramps the value across several frames
        // before it crosses the 0.1 deadzone in Listen(). The result is
        // visible weapon-switch delay per scroll click — issue #45 "QS
        // delay" on the public repo. GetAxisRaw returns the unsmoothed
        // per-frame delta, so a click triggers Next/PrevWeapon on the same
        // frame. Leave analog channels (Mouse X/Y, gamepad sticks) on
        // GetAxis so look/aim smoothing is preserved.
        if (_axis.StartsWith("Mouse ScrollWheel"))
        {
            return Input.GetAxisRaw(_axis);
        }
        return Input.GetAxis(_axis);
    }

    public override string ToString()
    {
        return _axis;
    }

    public override bool Equals(object obj)
    {
        if (obj is AxisInputChannel)
        {
            AxisInputChannel a = obj as AxisInputChannel;
            if (a.Axis == Axis)
                return true;
        }

        return false;
    }

    public override int GetHashCode()
    {
        return base.GetHashCode();
    }

    #region FIELDS

    private string _axis = string.Empty;
    private string _axisName = string.Empty;
    private float _value = 0;
    private float _lastValue = 0;
    private float _deadRange = 0.1f;
    private AxisReadingMethod _axisReading;

    #endregion

    #region PROPERTIES

    public string Axis
    {
        get { return _axis; }
    }
    public string Name
    { get { return _axisName; } }
    public float Value
    {
        get { return _value; }
        set { _value = _lastValue = value; }
    }
    public InputChannelType ChannelType
    {
        get { return InputChannelType.Axis; }
    }
    public bool IsPressed
    {
        get { return _value != 0; }
    }
    public bool IsChanged
    {
        get { return _lastValue != _value; }
    }

    #endregion

    public enum AxisReadingMethod
    {
        All = 0,
        PositiveOnly = 1,
        NegativeOnly = 2,
    }

    #region IByteArray Members

    public int FromBytes(byte[] bytes, int idx)
    {
        _axis = DefaultByteConverter.ToString(bytes, ref idx);
        _deadRange = DefaultByteConverter.ToFloat(bytes, ref idx);
        _axisReading = (AxisReadingMethod)DefaultByteConverter.ToInt(bytes, ref idx);

        switch (_axisReading)
        {
            case AxisReadingMethod.NegativeOnly: _axisName = _axis + " Up"; break;
            case AxisReadingMethod.PositiveOnly: _axisName = _axis + " Down"; break;
            default: _axisName = _axis; break;
        }

        return idx;
    }

    public byte[] GetBytes()
    {
        List<byte> bytes = new List<byte>();
        DefaultByteConverter.FromString(_axis, ref bytes);
        DefaultByteConverter.FromFloat(_deadRange, ref bytes);
        DefaultByteConverter.FromInt((int)_axisReading, ref bytes);

        return bytes.ToArray();
    }

    #endregion
}

public class ButtonInputChannel : IInputChannel
{
    public ButtonInputChannel(byte[] bytes, ref int idx)
    {
        idx = FromBytes(bytes, idx);
    }

    public ButtonInputChannel(string button)
    {
        _button = button;
    }

    public void Listen()
    {
        _wasDown = _isDown;
        _isDown = Input.GetButton(_button);
    }

    public void Reset()
    {
        _wasDown = false;
        _isDown = false;
    }

    public float RawValue()
    {
        return Input.GetButton(_button) ? 1 : 0;
    }

    public override string ToString()
    {
        return _button;
    }

    public override bool Equals(object obj)
    {
        if (obj is ButtonInputChannel)
        {
            ButtonInputChannel b = obj as ButtonInputChannel;
            if (b.Button == Button)
                return true;
        }

        return false;
    }

    public override int GetHashCode()
    {
        return base.GetHashCode();
    }

    #region FIELDS

    private bool _isDown = false;
    private bool _wasDown = false;
    private string _button = string.Empty;

    #endregion

    #region PROPERTIES

    public string Button
    {
        get { return _button; }
    }

    public string Name
    {
        get { return _button; }
    }

    public float Value
    {
        get { return _isDown ? 1 : 0; }
    }

    public InputChannelType ChannelType
    {
        get { return InputChannelType.Axis; }
    }

    public bool IsPressed
    {
        get { return _isDown; }
    }

    public bool IsChanged
    {
        get { return _wasDown != _isDown; }
    }

    #endregion

    #region IByteArray Members

    public int FromBytes(byte[] bytes, int idx)
    {
        _button = DefaultByteConverter.ToString(bytes, ref idx);

        return idx;
    }

    public byte[] GetBytes()
    {
        List<byte> bytes = new List<byte>();
        DefaultByteConverter.FromString(_button, ref bytes);
        return bytes.ToArray();
    }

    #endregion
}

//public class MouseMovementChannel : IInputChannel, IByteArray
//{
//    public MouseMovementChannel(byte[] bytes, ref int idx)
//    {
//        idx = FromBytes(bytes, idx);
//    }

//    public MouseMovementChannel(string name, int index)
//    {
//        Index = index;
//        Name = name;
//    }

//    public void Listen()
//    { }

//    public void Reset()
//    { }

//    public float RawValue()
//    {
//        return MouseInputAxis.Instance.Delta[Index];
//    }

//    public override string ToString()
//    {
//        return string.Format("Mouse Axis {0}", Index);
//    }

//    public override bool Equals(object obj)
//    {
//        if (obj is MouseMovementChannel)
//        {
//            MouseMovementChannel m = obj as MouseMovementChannel;
//            if (m.Index == Index)
//                return true;
//        }

//        return false;
//    }

//    public override int GetHashCode()
//    {
//        return base.GetHashCode();
//    }

//    public int Index { get; private set; }

//    #region FIELDS
//    private float _lastPosition = 0;
//    private int _frameCount;
//    #endregion

//    #region PROPERTIES

//    public InputChannelType ChannelType
//    {
//        get { return InputChannelType.Mouse; }
//    }

//    public float Value { get { return MouseInputAxis.Instance.Delta[Index]; } }

//    public bool IsChanged
//    {
//        get { return Value != 0; }
//    }
//    #endregion

//    #region IByteArray Members

//    public int FromBytes(byte[] bytes, int idx)
//    {
//        Index = DefaultByteConverter.ToInt(bytes, ref idx);
//        return idx;
//    }

//    public byte[] GetBytes()
//    {
//        List<byte> bytes = new List<byte>(4);
//        DefaultByteConverter.FromInt(Index, ref bytes);
//        return bytes.ToArray();
//    }

//    #endregion


//    public string Name
//    {
//        get;
//        private set;
//    }
//}