using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using KModkit;
using UnityEngine;

using Rnd = UnityEngine.Random;

public class WhiteButtonScript : MonoBehaviour
{
    public KMBombModule Module;
    public KMBombInfo BombInfo;
    public KMAudio Audio;
    public KMSelectable WhiteButtonSelectable;
    public GameObject WhiteButtonCap;
    public TextMesh[] ColorTexts;
    public TextMesh[] ValueTexts;
    public GameObject[] BlobObjs;

    private int _moduleId;
    private static int _moduleIdCounter = 1;
    private bool _moduleSolved;

    private readonly string[] COLORNAMES = { "Iridium", "East Bay", "Cerulean", "Laurel", "Celadon", "Seaport", "Apple", "Emerald", "Pelorous", "Lotus", "Plum", "Orchid", "Sycamore", "Battleship", "Cove", "Atlantis", "Pistachio", "Neptune", "Mahogany", "Mulberry", "Amethyst", "Sienna", "Puce", "Viola", "Turmeric", "Pine", "Silver" };
    private readonly string[] CBNAMES = { "Irid", "East", "Ceru", "Laur", "Cela", "Seap", "Apple", "Emer", "Pelo", "Lotus", "Plum", "Orch", "Syca", "Batt", "Cove", "Atla", "Pist", "Nept", "Mahog", "Mulb", "Ameth", "Sien", "Puce", "Viola", "Turm", "Pine", "Silv" };

    private Coroutine _timerTickCheck;
    private int _holdCount;
    private int? _initialHold = null;
    private List<int> _input = new List<int>();
    private int[] _solution = new int[3];
    private int[] _colorBlobs = new int[2];
    private int[][] _colorValues = new int[2][] { new int[3], new int[3] };
    private bool _isAnimating;
    private bool _ignored;

    private void Start()
    {
        _moduleId = _moduleIdCounter++;
        WhiteButtonSelectable.OnInteract += WhiteButtonPress;
        WhiteButtonSelectable.OnInteractEnded += WhiteButtonRelease;
        _colorBlobs = Enumerable.Range(0, 26).ToArray().Shuffle().Take(2).ToArray();

        for (int i = 0; i < 2; i++)
        {
            _colorValues[i][0] = _colorBlobs[i] / 9;
            _colorValues[i][1] = _colorBlobs[i] % 9 / 3;
            _colorValues[i][2] = _colorBlobs[i] % 3;
            var color = new Color(
                ((60 + 70 * ((float)_colorValues[i][0])) / 255),
                ((60 + 70 * ((float)_colorValues[i][1])) / 255),
                ((60 + 70 * ((float)_colorValues[i][2])) / 255)
                );
            BlobObjs[i].GetComponent<MeshRenderer>().material.color = color;
            ColorTexts[i].text = CBNAMES[_colorBlobs[i]];
        }

        _solution[0] = _colorValues[0][0] == _colorValues[1][0] ? 1 : (_colorValues[0][0] == _colorValues[1][0] + 1) || (_colorValues[0][0] + 2 == _colorValues[1][0]) ? 0 : 2;
        _solution[1] = _colorValues[0][1] == _colorValues[1][1] ? 1 : (_colorValues[0][1] == _colorValues[1][1] + 1) || (_colorValues[0][1] + 2 == _colorValues[1][1]) ? 0 : 2;
        _solution[2] = _colorValues[0][2] == _colorValues[1][2] ? 1 : (_colorValues[0][2] == _colorValues[1][2] + 1) || (_colorValues[0][2] + 2 == _colorValues[1][2]) ? 0 : 2;
        Debug.LogFormat("[The White Button #{0}] The two colors are {1} and {2}. The solution is {3}.", _moduleId, COLORNAMES[_colorBlobs[0]], COLORNAMES[_colorBlobs[1]], _solution.Select(i => i == 0 ? "-" : i == 1 ? "0" : "+").Join(""));
    }

    private bool WhiteButtonPress()
    {
        StartCoroutine(AnimateButton(0f, -0.05f));
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.BigButtonPress, transform);
        if (_moduleSolved)
            return false;
        _ignored = false;
        _timerTickCheck = StartCoroutine(TimerTickCheck());
        return false;
    }

    private void WhiteButtonRelease()
    {
        StartCoroutine(AnimateButton(-0.05f, 0f));
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.BigButtonRelease, transform);
        if (_moduleSolved || _isAnimating)
            return;
        if (_timerTickCheck != null)
            StopCoroutine(_timerTickCheck);
        if (_holdCount >= 4)
        {
            Debug.LogFormat("[The White Button #{0}] Held the button for at least 4 timer ticks. Issuing a reset.", _moduleId);
            _isAnimating = true;
            StartCoroutine(Reset());
            return;
        }
        if (_initialHold == null)
        {
            if (_holdCount == 0)
            {
                Debug.LogFormat("[The White Button #{0}] The first input was a zero. Ignoring.", _moduleId);
                _ignored = true;
                return;
            }
            _initialHold = _holdCount;
            ValueTexts[0].text = _initialHold.ToString();
            return;
        }
        int diff = _holdCount == _initialHold ? 1 : _holdCount < _initialHold ? 0 : 2;
        _input.Add(diff);
        ValueTexts[_input.Count].text = "-0+"[diff].ToString();
        Debug.LogFormat("[The White Button #{0}] Held the button over {1} timer tick{2}.", _moduleId, _holdCount, _holdCount == 1 ? "" : "s");
        if (_input.Count == 3)
        {
            _isAnimating = true;
            StartCoroutine(CheckAnswer());
        }
    }

    private IEnumerator AnimateButton(float a, float b)
    {
        var duration = 0.1f;
        var elapsed = 0f;
        while (elapsed < duration)
        {
            WhiteButtonCap.transform.localPosition = new Vector3(0f, Easing.InOutQuad(elapsed, a, b, duration), 0f);
            yield return null;
            elapsed += Time.deltaTime;
        }
        WhiteButtonCap.transform.localPosition = new Vector3(0f, b, 0f);
    }

    private IEnumerator TimerTickCheck()
    {
        _holdCount = 0;
        while (true)
        {
            int time = (int)BombInfo.GetTime() % 10;
            while (time == (int)BombInfo.GetTime() % 10)
                yield return null;
            _holdCount++;
        }
    }

    private IEnumerator Reset()
    {
        _initialHold = null;
        _holdCount = 0;
        _input = new List<int>();
        for (int i = 3; i >= 0; i--)
        {
            ValueTexts[i].text = "";
            yield return new WaitForSeconds(0.15f);
        }
        _isAnimating = false;
    }

    private IEnumerator CheckAnswer()
    {
        bool correct = true;
        for (int i = 0; i < _input.Count; i++)
        {
            if (_input[i] != _solution[i])
                correct = false;
        }
        if (correct)
        {
            _moduleSolved = true;
            Module.HandlePass();
            Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.CorrectChime, transform);
            Debug.LogFormat("[The White Button #{0}] Correctly submitted {1}. Module solved.", _moduleId, _input.Select(i => i == 0 ? "-" : i == 1 ? "0" : "+").Join(""));
        }
        else
        {
            Module.HandleStrike();
            Debug.LogFormat("[The White Button #{0}] Incorrectly submitted {1}. Strike.", _moduleId, _input.Select(i => i == 0 ? "-" : i == 1 ? "0" : "+").Join(""));
            _input = new List<int>();
            _initialHold = null;
            yield return new WaitForSeconds(1f);
            for (int i = 3; i >= 0; i--)
            {
                ValueTexts[i].text = "";
                yield return new WaitForSeconds(0.15f);
            }
            _isAnimating = false;
        }
    }

#pragma warning disable 0414
    private readonly string TwitchHelpMessage = "!{0} submit 2 3 1 0 [Hold for 2, 3, 1, and 0 timer ticks.] | !{0} submit 4 [Hold for 4 timer ticks, resets the module.]";
#pragma warning restore 0414

    private IEnumerator ProcessTwitchCommand(string command)
    {
        var parameters = command.ToUpperInvariant().Split(' ');
        var m = Regex.Match(parameters[0], @"^\s*submit\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!m.Success)
            yield break;
        var list = new List<int>();
        for (int i = 1; i < parameters.Count(); i++)
        {
            int val;
            if (!int.TryParse(parameters[i], out val) || val < 0 || val > 4)
                yield break;
            list.Add(val);
        }
        yield return null;
        for (int i = 0; i < list.Count; i++)
        {
            WhiteButtonSelectable.OnInteract();
            int time = (int)BombInfo.GetTime();
            while (Math.Abs(time - (int)BombInfo.GetTime()) != list[i])
                yield return null;
            WhiteButtonSelectable.OnInteractEnded();
            if (_ignored)
            {
                yield return "sendtochat The button was held over zero timer ticks for the intial value. Stopping command.";
                yield break;
            }
        }
        yield break;
    }

    public IEnumerator TwitchHandleForcedSolve()
    {
        int time = (int)BombInfo.GetTime();
        if (_initialHold == 3)
        {
            WhiteButtonSelectable.OnInteract();
            time = (int)BombInfo.GetTime();
            while (time - 4 != (int)BombInfo.GetTime())
                yield return null;
            WhiteButtonSelectable.OnInteractEnded();
            goto next;
        }
        if (_initialHold != null)
        {
            int index;
            for (index = 0; index < _input.Count; index++)
            {
                if (_input[index] != _solution[index])
                {
                    WhiteButtonSelectable.OnInteract();
                    time = (int)BombInfo.GetTime();
                    while (time - 4 != (int)BombInfo.GetTime())
                        yield return null;
                    WhiteButtonSelectable.OnInteractEnded();
                    goto next;
                }
            }
        }
        next:
        WhiteButtonSelectable.OnInteract();
        time = (int)BombInfo.GetTime();
        while (time - 1 != (int)BombInfo.GetTime())
            yield return null;
        WhiteButtonSelectable.OnInteractEnded();
        while (_input.Count < 3)
        {
            WhiteButtonSelectable.OnInteract();
            if (_solution[_input.Count] == 0)
                WhiteButtonSelectable.OnInteractEnded();
            else if (_solution[_input.Count] == 1)
            {
                while (_holdCount != _initialHold)
                    yield return null;
                WhiteButtonSelectable.OnInteractEnded();
            }
            else
            {
                while (_holdCount <= _initialHold)
                    yield return null;
                WhiteButtonSelectable.OnInteractEnded();
            }
            yield return null;
        }
    }
}
