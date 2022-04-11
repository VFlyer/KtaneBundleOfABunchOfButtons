using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BunchOfButtons;
using KModkit;
using TealButton;
using UnityEngine;

using Rnd = UnityEngine.Random;

public class TealButtonScript : MonoBehaviour
{
    public KMBombModule Module;
    public KMBombInfo BombInfo;
    public KMAudio Audio;
    public KMRuleSeedable RuleSeedable;
    public KMSelectable ButtonSelectable;
    public GameObject ButtonCap;
    public TextMesh ButtonText;
    public TextMesh[] MainTexts;
    public Color[] _textColors;

    private static readonly Dictionary<int, int[]> _ruleSeededTables = new Dictionary<int, int[]>();

    private static int _moduleIdCounter = 1;
    private int _moduleId;
    private bool _moduleSolved;

    private int[] _inputs = new int[3] { 9, 9, 9 };
    private readonly int[] _solutions = new int[3];
    private int _currentIx;
    private Coroutine _strike;
    private bool _isStriking;

    private void Start()
    {
        _moduleId = _moduleIdCounter++;

        // START RULE SEED
        var rnd = RuleSeedable.GetRNG();
        if (rnd.Seed != 1)
            Debug.LogFormat("[The Teal Button #{0}] Using rule seed: {1}.", _moduleId, rnd.Seed);
        for (var i = 0; i < 73; i++)
            rnd.Next(0, 2);

        var directions = (TealDirection[])Enum.GetValues(typeof(TealDirection));
        var direction = directions[rnd.Next(0, 4)];

        int[] pairPos = { 0, 1, 2, 3, 4, 5 };
        rnd.ShuffleFisherYates(pairPos);

        var letterTable = _ruleSeededTables.ContainsKey(rnd.Seed)
            ? _ruleSeededTables[rnd.Seed]
            : (_ruleSeededTables[rnd.Seed] = LatinSquare.Generate(rnd, 9));
        // END RULE SEED

        var snDigits = BombInfo.GetSerialNumber().Select(ch => (ch >= '0' && ch <= '9' ? ch - '0' : ch - 'A' + 1) % 9).ToArray();
        var buttonNum = Rnd.Range(0, 9);
        ButtonText.text = buttonNum.ToString();

        var snPairs = new int[6];
        for (int i = 0; i < snPairs.Length; i++)
            snPairs[i] = snDigits[pairPos[i]];
        for (int i = 0; i < _solutions.Length; i++)
            _solutions[i] = letterTable[
                ((snPairs[i * 2] + (direction == TealDirection.Left ? 9 - buttonNum : direction == TealDirection.Right ? buttonNum : 0)) % 9) +
                9 * ((snPairs[i * 2 + 1] + (direction == TealDirection.Up ? 9 - buttonNum : direction == TealDirection.Down ? buttonNum : 0)) % 9)];
        Debug.LogFormat("[The Teal Button #{0}] The number printed on the button is {1}.", _moduleId, buttonNum);
        Debug.LogFormat("[The Teal Button #{0}] The solution is {1}.", _moduleId, _solutions.Select(ch => (char)('A' + ch)).Join(""));

        MainTexts[_currentIx].color = _textColors[3];
        ButtonSelectable.OnInteract += ButtonPress;
        ButtonSelectable.OnInteractEnded += ButtonRelease;
    }

    private bool ButtonPress()
    {
        StartCoroutine(AnimateButton(0f, -0.05f));
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.BigButtonPress, transform);
        if (_moduleSolved || _isStriking)
            return false;
        if ((int)BombInfo.GetTime() % 10 == 0)
        {
            bool correct = true;
            for (int i = 0; i < 3; i++)
            {
                if (_solutions[i] != _inputs[i])
                {
                    correct = false;
                    MainTexts[i].color = _textColors[0];
                }
                else
                    MainTexts[i].color = _textColors[1];
            }
            if (correct)
            {
                if (_strike != null)
                    StopCoroutine(_strike);
                _moduleSolved = true;
                Module.HandlePass();
                Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.CorrectChime, transform);
                Debug.LogFormat("[The Teal Button #{0}] Correctly inputted {1}. Module solved.", _moduleId, _inputs.Select(i => "ABCDEFGHI-"[i]).Join(""));
            }
            else
            {
                _isStriking = true;
                if (_strike != null)
                    StopCoroutine(_strike);
                _strike = StartCoroutine(Strike());
                Module.HandleStrike();
                Debug.LogFormat("[The Teal Button #{0}] Incorrectly inputted {1}. Strike.", _moduleId, _inputs.Select(i => "ABCDEFGHI-"[i]).Join(""));
            }
        }
        else
        {
            _inputs[_currentIx] = (int)BombInfo.GetTime() % 10 - 1;
            MainTexts[_currentIx].text = "ABCDEFGHI-"[_inputs[_currentIx]].ToString();
        }
        _currentIx = (_currentIx + 1) % 3;
        if (!_isStriking && !_moduleSolved)
        {
            for (int i = 0; i < 3; i++)
                MainTexts[i].color = _textColors[i == _currentIx ? 3 : 2];
        }
        return false;
    }

    private void ButtonRelease()
    {
        StartCoroutine(AnimateButton(-0.05f, 0f));
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.BigButtonRelease, transform);
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

    private IEnumerator Strike()
    {
        yield return new WaitForSeconds(2f);
        for (int i = 0; i < 3; i++)
            MainTexts[i].color = _textColors[i == _currentIx ? 3 : 2];
        _isStriking = false;
    }

#pragma warning disable 414
    private readonly string TwitchHelpMessage = @"!{0} press 4 5 3 0 [Press the button when the last digit of the timer is 4, 5, 3 and 0.] |";
#pragma warning restore 414

    public IEnumerator ProcessTwitchCommand(string command)
    {
        var parameters = command.ToUpperInvariant().Split();
        var m = Regex.Match(parameters[0], @"^\s*press\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!m.Success)
            yield break;
        if (parameters.Length < 2)
            yield break;
        var list = new List<int>();
        for (int i = 1; i < parameters.Length; i++)
        {
            int val;
            if (!int.TryParse(parameters[i], out val) || val < 0 || val > 9)
                yield break;
            list.Add(val);
        }
        yield return null;
        for (int i = 0; i < list.Count; i++)
        {
            while ((int)BombInfo.GetTime() % 10 != list[i])
                yield return null;
            ButtonSelectable.OnInteract();
            yield return new WaitForSeconds(0.1f);
            ButtonSelectable.OnInteractEnded();
            yield return new WaitForSeconds(0.1f);
        }
    }

    public IEnumerator TwitchHandleForcedSolve()
    {
        for (int i = 0; i < 3; i++)
        {
            if (_inputs[_currentIx] != _solutions[_currentIx])
            {
                while ((int)BombInfo.GetTime() % 10 != _solutions[_currentIx] + 1)
                    yield return true;
                ButtonSelectable.OnInteract();
                yield return new WaitForSeconds(0.1f);
                ButtonSelectable.OnInteractEnded();
                yield return new WaitForSeconds(0.1f);
            }
        }
        while ((int)BombInfo.GetTime() % 10 != 0)
            yield return true;
        ButtonSelectable.OnInteract();
        yield return new WaitForSeconds(0.1f);
        ButtonSelectable.OnInteractEnded();
    }
}
