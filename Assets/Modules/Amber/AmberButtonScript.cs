using KModkit;
using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using Rnd = UnityEngine.Random;

public class AmberButtonScript : MonoBehaviour
{
    public KMBombModule Module;
    public KMBombInfo BombInfo;
    public KMAudio Audio;
    public KMSelectable ButtonSelectable;
    public TextMesh AmberText;
    public GameObject ButtonCap;

    private static int _moduleIdCounter = 1;
    private readonly int[] _order = { 0, 1, 2, 3, 4, 5 };
    private int _moduleId, _lastTimerSeconds, _current, _typed;
    private int _toCycle = 4;
    private string _serialNumber, _input;
    private readonly bool[] _done = new bool[2];
    private bool _moduleSolved, _buttonHeld, _checkHold, _rotated, _isRotating, _typing;

    private string ToBinary(char y)
    {
        return Convert.ToString("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ".IndexOf(y), 2).PadLeft(6, '0').Select(x => x == '0' ? '?' : '¿').Join("");
    }

    private void Start()
    {
        _moduleId = _moduleIdCounter++;
        ButtonSelectable.OnInteract += ButtonPress;
        ButtonSelectable.OnInteractEnded += ButtonRelease;
        _serialNumber = BombInfo.GetSerialNumber();
        _order.Shuffle();
        if (_order[4] > _order[5])
        {
            int cache = _order[4];
            _order[4] = _order[5];
            _order[5] = cache;
        }
        Debug.LogFormat("[The Amber Button #{0}] The displayed serial number characters are {1}, {2} and {3}.", _moduleId,
            _serialNumber[_order[0]], _serialNumber[_order[1]], _serialNumber[_order[2]], _serialNumber[_order[3]]);
        Debug.LogFormat("[The Amber Button #{0}] The two missing characters are {1} and {2}, or {3} and {4}.", _moduleId,
            _serialNumber[_order[4]], _serialNumber[_order[5]], ToBinary(_serialNumber[_order[4]]), ToBinary(_serialNumber[_order[5]]));
        AmberText.text = "";
        Module.OnActivate += delegate { AmberText.text = ToBinary(_serialNumber[_order[_current]]); };
    }

    private void Update()
    {
        var seconds = (int) BombInfo.GetTime() % 10;
        if (seconds != _lastTimerSeconds && !_moduleSolved)
        {
            _lastTimerSeconds = seconds;
            _current = (_current + 1) % _toCycle;
            if (!_typing)
                AmberText.text = ToBinary(_serialNumber[_order[_current]]);
            if (_buttonHeld)
                _checkHold = true;
        }
    }

    private bool ButtonPress()
    {
        StartCoroutine(AnimateButton(0f, -0.05f));
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.BigButtonPress, transform);
        if (!_moduleSolved)
        {
            _checkHold = false;
            _buttonHeld = true;
        }
        return false;
    }

    private void ButtonRelease()
    {
        StartCoroutine(AnimateButton(-0.05f, 0f));
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.BigButtonRelease, transform);
        if (!_moduleSolved)
        {
            _buttonHeld = false;
            if (_checkHold)
            {
                if (!_isRotating)
                    StartCoroutine(RotateButton());
            }
            else
                Pressed();
        }
    }

    private void Pressed()
    {
        _typing = true;
        _input += _rotated ? "¿" : "?";
        AmberText.text = _input.PadRight(6, '-');
        _typed++;
        if (_typed == 6)
        {
            _typing = false;
            if (AmberText.text == ToBinary(_serialNumber[_order[4]]) && !_done[0])
            {
                StartCoroutine(StageAnimation(new Color32(255, 233, 171, 255)));
                Debug.LogFormat("[The Amber Button #{0}] You submitted {1}, which was correct.", _moduleId, AmberText.text);
                _done[0] = true;
                _toCycle = 5;
            }
            else if (AmberText.text == ToBinary(_serialNumber[_order[5]]) && !_done[0])
            {
                StartCoroutine(StageAnimation(new Color32(255, 233, 171, 255)));
                Debug.LogFormat("[The Amber Button #{0}] You submitted {1}, which was correct.", _moduleId, AmberText.text);
                _done[0] = true;
                _toCycle = 5;
                var cache = _order[4];
                _order[4] = _order[5];
                _order[5] = cache;
            }
            else if (AmberText.text == ToBinary(_serialNumber[_order[5]]) && !_done[1])
                _done[1] = true;
            else
                Module.HandleStrike();
            if (_done[0] && _done[1])
            {
                Module.HandlePass();
                Debug.LogFormat("[The Amber Button #{0}] You submitted {1}, which was correct. Module solved!", _moduleId, AmberText.text);
                StartCoroutine(StageAnimation(new Color32(0, 0, 0, 255)));
                _moduleSolved = true;
            }
            _typed = 0;
            _input = "";
        }
    }

    private IEnumerator AnimateButton(float a, float b)
    {
        var duration = 0.1f;
        var elapsed = 0f;
        while (elapsed < duration)
        {
            ButtonCap.transform.localPosition = new Vector3(0f, Easing.InOutQuad(elapsed, a, b, duration), 0f);
            yield return null;
            elapsed += Time.deltaTime;
        }
        ButtonCap.transform.localPosition = new Vector3(0f, b, 0f);
    }

    private IEnumerator StageAnimation(Color32 colour)
    {
        Audio.PlaySoundAtTransform("AmberButtonStage", ButtonCap.transform);
        var duration = 0.5f;
        var elapsed = 0f;
        while (elapsed < duration)
        {
            AmberText.color = Color.Lerp(new Color(0, 1, 0), colour, elapsed * 2);
            yield return null;
            elapsed += Time.deltaTime;
        }
        AmberText.color = colour;
    }

    private IEnumerator RotateButton()
    {
        _isRotating = true;
        Audio.PlaySoundAtTransform("AmberButtonUnscrew", ButtonCap.transform);
        var duration = 0.3f;
        var elapsed = 0f;
        _rotated = !_rotated;
        while (elapsed < duration)
        {
            ButtonCap.transform.localEulerAngles = new Vector3(0f, Easing.InOutQuad(elapsed, !_rotated ? 180 : 0, !_rotated ? 360 : 180, duration), 0f);
            yield return null;
            elapsed += Time.deltaTime;
        }
        ButtonCap.transform.localEulerAngles = new Vector3(0f, !_rotated ? 0 : 180, 0f);
        _isRotating = false;
    }

#pragma warning disable 0414
    private readonly string TwitchHelpMessage = "!{0} hold tap [hold the button over a timer tick, then tap it]";
#pragma warning restore 0414

    private IEnumerator ProcessTwitchCommand(string command)
    {
        command = command.ToLowerInvariant();
        string[] commandArray = command.Split(' ');
        if (_moduleSolved)
            yield break;
        for (int i = 0; i < commandArray.Length; i++)
            if (commandArray[i] != "hold" && commandArray[i] != "tap" && commandArray[i] != "h" && commandArray[i] != "t")
            {
                yield return "sendtochaterror Invalid command.";
                yield break;
            }
        for (int i = 0; i < commandArray.Length; i++)
        {
            ButtonSelectable.OnInteract();
            if (commandArray[i] == "hold" || commandArray[i] == "h")
                while (!_checkHold)
                    yield return new WaitForSeconds(0.1f);
            ButtonSelectable.OnInteractEnded();
            yield return new WaitForSeconds(0.1f);
        }
    }

    public IEnumerator TwitchHandleForcedSolve()
    {
        var cache2 = _rotated ? '¿' : '?';
        while (!_moduleSolved)
        {
            ButtonSelectable.OnInteract();
            if (ToBinary(_serialNumber[_order[_toCycle]])[_typed] != cache2)
            {
                while (!_checkHold)
                    yield return new WaitForSeconds(0.1f);
                cache2 = _rotated ? '?' : '¿';
            }
            ButtonSelectable.OnInteractEnded();
            yield return new WaitForSeconds(0.1f);
        }
    }
}
