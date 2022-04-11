using System;
using System.Collections;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Text.RegularExpressions;
using UnityEngine;
using Rnd = UnityEngine.Random;

public class CyanButtonScript : MonoBehaviour
{
    public KMBombModule Module;
    public KMBombInfo BombInfo;
    public KMAudio Audio;
    public KMSelectable CyanButtonSelectable;
    public GameObject CyanButtonCap;
    public GameObject CyanButtonObj;
    public GameObject CyanButtonSelObj;
    public GameObject[] LeftDoors, RightDoors;
    public TextMesh CyanScreenText;

    private static int _moduleIdCounter = 1;
    private int _moduleId;
    private int _currentStage;
    private readonly int[] _buttonPositions = new int[6];
    private readonly bool[] _correctPresses = new bool[6];
    private bool _moduleSolved;
    private bool _buttonVisible = true;
    private Coroutine _timer;
    private int _timerTime = 20;
    private bool _autoSolving;

    private static readonly float[] xPos = { -0.05f, 0f, 0.05f, -0.05f, 0f, 0.05f };
    private static readonly float[] zPos = { 0f, 0f, 0f, -0.05f, -0.05f, -0.05f };

    private void Start()
    {
        _moduleId = _moduleIdCounter++;
        CyanButtonSelectable.OnInteract += CyanButtonPress;
        CyanButtonSelectable.OnInteractEnded += CyanButtonRelease;
        GenerateButtonSequence();
    }

    private bool CyanButtonPress()
    {
        StartCoroutine(AnimateButton(0f, -0.05f));
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.BigButtonPress, transform);
        return false;
    }

    private void CyanButtonRelease()
    {
        StartCoroutine(AnimateButton(-0.05f, 0f));
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.BigButtonRelease, transform);
        if (!_moduleSolved && _buttonVisible)
            DoButtonLogic(true);
    }

    private void GenerateButtonSequence()
    {
        for (int i = 0; i < 6; i++)
            _buttonPositions[i] = Rnd.Range(0, 6);
        CyanButtonObj.transform.localPosition = new Vector3(xPos[_buttonPositions[_currentStage]], 0.02f, zPos[_buttonPositions[_currentStage]]);
        _correctPresses[0] = true;
        _correctPresses[1] =
            _buttonPositions[0] < 3 ||
            _buttonPositions[1] > 2;
        _correctPresses[2] =
            _buttonPositions[2] % 3 != 1 &&
            (_buttonPositions[0] % 3 != 2 && _buttonPositions[1] % 3 != 2);
        _correctPresses[3] =
            _buttonPositions[3] % 3 != 0 !=
            (_buttonPositions[0] == 2 || _buttonPositions[1] == 2 || _buttonPositions[2] == 2);
        _correctPresses[4] =
            !(_buttonPositions[4] % 3 == 2 &&
            (_correctPresses[3] ||
            (!_correctPresses[1] && !_correctPresses[2])));
        _correctPresses[5] =
            (_buttonPositions[5] != 0 && _buttonPositions[5] != 5) ==
            (((_correctPresses[1] && _buttonPositions[1] < 3) ||
            (_correctPresses[4] && _buttonPositions[4] < 3)) &&
            _buttonPositions[0] != 0 &&
            _buttonPositions[1] != 1 &&
            _buttonPositions[2] != 2 &&
            _buttonPositions[3] != 3 &&
            _buttonPositions[4] != 4 &&
            _buttonPositions[5] != 5
            );

        Debug.LogFormat("[The Cyan Button #{0}] The stages in which the button must be pressed are: {1}.",
            _moduleId, Enumerable.Range(0, 6).Where(st => _correctPresses[st]).Select(i => i + 1).Join(", "));
    }

    private IEnumerator StartTimer()
    {
        yield return new WaitForSeconds(1.6f);
        for (int i = _timerTime; i > 0; i--)
        {
            CyanScreenText.text = i.ToString("00");
            yield return new WaitForSeconds(1f);
        }
        DoButtonLogic(false);
    }

    private void DoButtonLogic(bool pressed)
    {
        if (_timer != null)
            StopCoroutine(_timer);
        int prevPos = _buttonPositions[_currentStage];
        CyanScreenText.text = "--";
        if (_correctPresses[_currentStage] != pressed)
        {
            if (pressed)
                Debug.LogFormat("[The Cyan Button #{0}] You pressed button {1}, when you should have let the timer run out. Strike.", _moduleId, _currentStage + 1);
            else
                Debug.LogFormat("[The Cyan Button #{0}] You let the timer run out at button {1}, when you should have pressed it. Strike.", _moduleId, _currentStage + 1);
            Module.HandleStrike();
            GenerateButtonSequence();
            _currentStage = 0;
        }
        else
        {
            _currentStage++;
            if (_currentStage < 6)
                _timer = StartCoroutine(StartTimer());
        }
        StartCoroutine(DoButtonMove(prevPos, _currentStage == 6 ? (int?) null : _buttonPositions[_currentStage]));
    }

    private IEnumerator DoButtonMove(int sinkInto, int? comeOutOf)
    {
        if (_moduleSolved)
            yield break;
        _buttonVisible = false;
        StartCoroutine(OpenDoors(sinkInto, false));
        StartCoroutine(MoveButton(sinkInto, false));
        yield return new WaitForSeconds(1f);
        if (comeOutOf != null)
        {
            StartCoroutine(OpenDoors(comeOutOf.Value, true));
            StartCoroutine(MoveButton(comeOutOf.Value, true));
        }
        else
        {
            _moduleSolved = true;
            Module.HandlePass();
            Debug.LogFormat("[The Cyan Button #{0}] You performed all correct button actions. Module solved.", _moduleId);
        }
    }

    private IEnumerator AnimateButton(float a, float b)
    {
        var duration = 0.1f;
        var elapsed = 0f;
        while (elapsed < duration)
        {
            CyanButtonCap.transform.localPosition = new Vector3(0f, Easing.InOutQuad(elapsed, a, b, duration), 0f);
            yield return null;
            elapsed += Time.deltaTime;
        }
        CyanButtonCap.transform.localPosition = new Vector3(0f, b, 0f);
    }

    private IEnumerator OpenDoors(int b, bool isEjecting)
    {
        var duration = 0.2f;
        var elapsed = 0f;
        Audio.PlaySoundAtTransform("DoorOpen", transform);
        while (elapsed < duration)
        {
            LeftDoors[b].transform.localEulerAngles = new Vector3(0f, 0f, Easing.InOutQuad(elapsed, 0f, 90f, duration));
            RightDoors[b].transform.localEulerAngles = new Vector3(0f, 0f, Easing.InOutQuad(elapsed, 0f, -90f, duration));
            yield return null;
            elapsed += Time.deltaTime;
        }
        LeftDoors[b].transform.localEulerAngles = new Vector3(0f, 0f, 90f);
        RightDoors[b].transform.localEulerAngles = new Vector3(0f, 0f, -90f);
        yield return new WaitForSeconds(0.2f);
        if (!isEjecting)
            yield return new WaitForSeconds(0.2f);
        var durationSecond = 0.2f;
        var elapsedSecond = 0f;
        while (elapsedSecond < durationSecond)
        {
            LeftDoors[b].transform.localEulerAngles = new Vector3(0f, 0f, Easing.InOutQuad(elapsedSecond, 90f, 0f, durationSecond));
            RightDoors[b].transform.localEulerAngles = new Vector3(0f, 0f, Easing.InOutQuad(elapsedSecond, -90f, 0f, durationSecond));
            yield return null;
            elapsedSecond += Time.deltaTime;
        }
        LeftDoors[b].transform.localEulerAngles = new Vector3(0f, 0f, 0f);
        RightDoors[b].transform.localEulerAngles = new Vector3(0f, 0f, 0f);
        Audio.PlaySoundAtTransform("DoorClose", transform);
        if (!isEjecting)
            CyanButtonObj.SetActive(false);
    }

    private IEnumerator MoveButton(int b, bool isEjecting)
    {
        var nextYPos = 0f;
        if (isEjecting)
        {
            nextYPos = 0.02f;
            CyanButtonObj.SetActive(true);
            yield return new WaitForSeconds(0.2f);
        }
        else
            CyanButtonSelObj.SetActive(false);
        var duration = 0.3f;
        var elapsed = 0f;
        while (elapsed < duration)
        {
            if (isEjecting)
                CyanButtonObj.transform.localPosition = new Vector3(xPos[b], Easing.InOutQuad(elapsed, 0f, 0.05f, duration), zPos[b]);
            else
                CyanButtonObj.transform.localPosition = new Vector3(xPos[b], Easing.InOutQuad(elapsed, 0.02f, 0.05f, duration), zPos[b]);
            yield return null;
            elapsed += Time.deltaTime;
        }
        CyanButtonObj.transform.localPosition = new Vector3(xPos[b], 0.05f, zPos[b]);
        var durationSecond = 0.3f;
        var elapsedSecond = 0f;
        while (elapsedSecond < durationSecond)
        {
            if (isEjecting)
                CyanButtonObj.transform.localPosition = new Vector3(xPos[b], Easing.InOutQuad(elapsedSecond, 0.05f, 0.02f, durationSecond), zPos[b]);
            else
                CyanButtonObj.transform.localPosition = new Vector3(xPos[b], Easing.InOutQuad(elapsedSecond, 0.05f, 0f, durationSecond), zPos[b]);
            yield return null;
            elapsedSecond += Time.deltaTime;
        }
        CyanButtonObj.transform.localPosition = new Vector3(xPos[b], nextYPos, zPos[b]);
        if (isEjecting)
        {
            CyanButtonSelObj.SetActive(true);
            _buttonVisible = true;
        }
    }
#pragma warning disable 0414
    private readonly string TwitchHelpMessage = "!{0} tap [tap the button]";
#pragma warning restore 0414

    private KMSelectable[] ProcessTwitchCommand(string command)
    {
        if (!_autoSolving)
            _timerTime = 30;
        return _moduleSolved || !Regex.IsMatch(command, @"^\s*(tap|press|submit|click)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ? null : new[] { CyanButtonSelectable };
    }

    public IEnumerator TwitchHandleForcedSolve()
    {
        _autoSolving = true;
        _timerTime = 3;
        while (!_moduleSolved && _correctPresses.Skip(_currentStage).Any(b => b))
        {
            if (_correctPresses[_currentStage])
            {
                while (!_buttonVisible)
                    yield return null;
                CyanButtonSelectable.OnInteract();
                yield return new WaitForSeconds(.1f);
                CyanButtonSelectable.OnInteractEnded();
                yield return null;
            }
            yield return null;
        }
        while (!_moduleSolved)
            yield return true;
    }
}
