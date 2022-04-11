using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using KModkit;
using System.Linq;
using System;
using System.Text.RegularExpressions;

public class MissingTextureButtonScript : MonoBehaviour {

	public MeshRenderer[] possibleRenderers;
    public MeshRenderer[] unusedRenderers;
    public string[] rendererType;
	public KMBombModule modSelf;
	public KMBombInfo bombInfo;
	public KMAudio Audio;
    public KMSelectable ButtonSelectable;
    public GameObject ButtonCap;
    public GameObject[] miscGameObjects;
    public KMRuleSeedable ruleSeed;
    public TextMesh textDisplay;

    private static int _moduleIdCounter = 1;
    private int _moduleId;
    private bool _moduleSolved, isHolding, confirmHolding, rsShiftRight;

    private float startInteractionTime = -1f, noInteractionTime = -1f, lastInteractionTime = -1f;
    private int mashCount, curColumnIdx, affectedRowIdx, correctActionsCount, expectedDigitRelease = -1, rsStartDigitIdx, rsHoldModifierDigitIdx;

    private int[,] correctInteractionIdxes;
    private int[,] timedInteractionIdx;
    private int[] colIDxArrangements = Enumerable.Range(0, 10).ToArray(), rowIdxArrangements = Enumerable.Range(0, 12).ToArray();
    List<int> usedIdxes = new List<int>();

    readonly string[] actionInteractions = { "H", "T", "2T", "3T", "4T", "5T", "6T", "7T", "8T", "9T", "10T", "11+T" },
        quirkHandlingString = { "'s material is missing", "'s visual disappears" },
        rsSerialNumberStartDigitObtain = { "last digit in serial no.",
        "first digit in serial no.",
        "2nd digit in serial no.",
        "3rd character in serial no.",
        "6th character in serial no." };

    private void HandleRuleSeed()
    {
        var oneRandom = ruleSeed != null ? ruleSeed.GetRNG() : new MonoRandom(1);
        correctInteractionIdxes = new int[12, 10];
        timedInteractionIdx = new int[12, 10];
        var possibleTimedInteractions = Enumerable.Repeat(-1, 10).Concat(Enumerable.Range(0, 10));
        var possibleBaseInteractions = Enumerable.Repeat(0, 9).Concat(Enumerable.Repeat(1, 9)).Concat(Enumerable.Range(2, 9));

        for (var row = 0; row < correctInteractionIdxes.GetLength(0); row++)
        {
            for (var col = 0; col < correctInteractionIdxes.GetLength(1); col++)
            {

                correctInteractionIdxes[row, col] = possibleBaseInteractions.ElementAt(oneRandom.Next(0, possibleBaseInteractions.Count()));
                timedInteractionIdx[row, col] = possibleTimedInteractions.ElementAt(oneRandom.Next(0, possibleTimedInteractions.Count()));
            }
        }
        rsShiftRight = oneRandom.Next(0, 2) == 0;
        oneRandom.ShuffleFisherYates(colIDxArrangements);
        var allDigitsObtainIdx = oneRandom.ShuffleFisherYates(Enumerable.Range(-1, 5).ToArray());

        rsStartDigitIdx = allDigitsObtainIdx.First();
        rsHoldModifierDigitIdx = allDigitsObtainIdx.ElementAt(1);

        QuickLog(string.Format("Generated rule seeded instructions with a seed of {0}. See filtered log for the full instructions.", oneRandom.Seed));
        QuickLog(string.Format("Direction to change when switching columns: {0}", rsShiftRight ? "Right" : "Left"), false);
        QuickLog(string.Format("Obtain the starting column by the {0}", rsSerialNumberStartDigitObtain[rsStartDigitIdx + 1]), false);
        QuickLog(string.Format("Obtain the modifier when holding by the {0}", rsSerialNumberStartDigitObtain[rsHoldModifierDigitIdx + 1]), false);
        QuickLog(string.Format("Columns' arranged digits from left to right: [{0}]", colIDxArrangements.Join(",")), false);
        QuickLog(string.Format("Table from top to bottom, left to right, (denoting for each quirk, top to bottom):", colIDxArrangements.Join(",")), false);
        for (var x = 0; x < rowIdxArrangements.Length; x++)
        {
            var curIdx = rowIdxArrangements[x];
            var result = "";
            for (var y = 0; y < correctInteractionIdxes.GetLength(1); y++)
            {
                var curCorrectActionInteractionIdx = correctInteractionIdxes[x, y];
                var curTimedInteractionIdx = timedInteractionIdx[x, y];
                if (y > 0)
                    result += ',';
                result += string.Format("{0}{1}", actionInteractions[curCorrectActionInteractionIdx], curTimedInteractionIdx == -1 ? "" : curTimedInteractionIdx.ToString());
            }

            QuickLog(string.Format("{0}{1}: {2}", rendererType[curIdx / 2], quirkHandlingString[curIdx % 2], result), false);
        }
    }

    private void RenderAbnormality(int idxValue)
    {
        var curIdxRenderer = idxValue / 2;
        if (curIdxRenderer < 0 || curIdxRenderer >= possibleRenderers.Length) return;
        if (idxValue % 2 == 1)
            possibleRenderers[curIdxRenderer].enabled = false;
        else
            possibleRenderers[curIdxRenderer].material = null;
    }

    int ObtainRuleSeedDigit(int rsIdx = -1)
    {
        var serialNoNumbers = bombInfo.GetSerialNumberNumbers();
        var serialNo = bombInfo.GetSerialNumber();
        switch (rsIdx)
        {
            case -1:
                return serialNoNumbers.LastOrDefault();
            case 0:
                return serialNoNumbers.FirstOrDefault();
            case 1:
                return serialNoNumbers.ElementAtOrDefault(1);
            case 2:
                return (serialNo[2] - '0') % 10;
            case 3:
                return (serialNo.Last() - '0') % 10;
        }
        return 0;
    }

    private void Start()
    {
        _moduleId = _moduleIdCounter++;
        ButtonSelectable.OnInteract += ButtonPress;
        ButtonSelectable.OnInteractEnded += ButtonRelease;
        HandleRuleSeed();
        curColumnIdx = Array.IndexOf(colIDxArrangements, ObtainRuleSeedDigit(rsStartDigitIdx));
        QuickLog(string.Format("Starting on col {0} from the left.", "ABCDEFGHIJ".ElementAtOrDefault(curColumnIdx)));
        var curActionRequired = correctInteractionIdxes[affectedRowIdx, curColumnIdx];
        var curTimedAction = timedInteractionIdx[affectedRowIdx, curColumnIdx];
        
        QuickLog(string.Format("Intersecting with the base statement gives the following result: {2} {3}",
            "ABCDEFGHIJ".ElementAtOrDefault(curColumnIdx), affectedRowIdx + 1,
            curActionRequired == 0 ? "Hold" : ("Tap " + curActionRequired + " time(s)"), curTimedAction == -1 ? "any time" : ("on last seconds digit " + curTimedAction)));
        textDisplay.text = "";
    }

    private void QuickLog(string value, bool visibleOnLFA = true)
    {
        if (visibleOnLFA)
            Debug.LogFormat("[The Missing Texture Button #{0}] {1}", _moduleId, value);
        else
            Debug.LogFormat("<The Missing Texture Button #{0}> {1}", _moduleId, value);
    }

    private bool ButtonPress()
    {
        StartCoroutine(AnimateButton(0f, -0.05f));
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.BigButtonPress, transform);
        if (!_moduleSolved)
        {
            if (startInteractionTime < 0f)
                startInteractionTime = bombInfo.GetTime();
            noInteractionTime = 0f;
            lastInteractionTime = bombInfo.GetTime();
            isHolding = true;
        }
        return false;
    }

    private void ButtonRelease()
    {
        StartCoroutine(AnimateButton(-0.05f, 0f));
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.BigButtonRelease, transform);
        if (!_moduleSolved)
        {
            isHolding = false;
            noInteractionTime = 0f;
            lastInteractionTime = bombInfo.GetTime();
            if (confirmHolding)
                ProcessHandledAction();
            else
                mashCount++;
        }
    }
    private void ProcessHandledAction()
    {
        var isCorrect = true;

        var curActionRequired = correctInteractionIdxes[affectedRowIdx, curColumnIdx];
        var curTimedAction = timedInteractionIdx[affectedRowIdx, curColumnIdx];
        var lastSecondsDigitStart = (int)(startInteractionTime % 10);
        var lastSecondsDigitLast = (int)(lastInteractionTime % 10);
        /*
        QuickLog(string.Format( "Using the cell at {0}{1} gives the following result: {2} {3}",
            "ABCDEFGHIJ".ElementAtOrDefault(curColumnIdx), affectedRowIdx + 1,
            curActionRequired == 0 ? "hold" : ("tap " + curActionRequired + " time(s)"), curTimedAction == -1 ? "any time" : ("on last seconds digit " + curTimedAction)));
        */
        if (mashCount > 0 && confirmHolding)
        {
            QuickLog("The button was tapped and then held. This is not acceptable form of input.");
            isCorrect = false;
        }
        else if (curActionRequired != mashCount && curActionRequired > 0)
        {
            QuickLog(string.Format("The button was {0} when it should've been {1}",
                mashCount == 0 ? "held" : ("tapped " + mashCount + " time(s)"),
                "tapped " + curActionRequired + " time(s)"));
            isCorrect = false;
        }
        else if (curActionRequired == 0 && !confirmHolding && mashCount > 0)
        {
            QuickLog("The button needed to be held and the user did not hold the button.");
            isCorrect = false;
        }
        else if (curActionRequired == 0 && confirmHolding && mashCount == 0 && lastSecondsDigitLast != expectedDigitRelease && expectedDigitRelease != -1)
        {
            QuickLog(string.Format("The button was correctly held but was released on a {0} instead of {1}", lastSecondsDigitLast, expectedDigitRelease));
            isCorrect = false;
        }
        if (curTimedAction != -1 && curTimedAction != lastSecondsDigitStart)
        {
            QuickLog(string.Format("The button was a timed {2} and you started {3} the button when the last seconds digit was a {0} rather than a {1}", curTimedAction, lastSecondsDigitStart, curActionRequired == 0 ? "hold" : "press", mashCount == 0 && confirmHolding ? "holding" : "pressing"));
            isCorrect = false;
        }
        textDisplay.text = "";
        if (isCorrect)
        {
            correctActionsCount++;
            if (correctActionsCount >= 4)
            {
                Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.CorrectChime, transform);
                QuickLog("Enough correct actions have been made overall. Module disarmed.");
                modSelf.HandlePass();
                StartCoroutine(HandleDisarmAnim());
                _moduleSolved = true;
                return;
            }
            curColumnIdx = PMod(curColumnIdx + (rsShiftRight ? 1 : -1), colIDxArrangements.Length);
            QuickLog(string.Format("Correct actions made: {0}",correctActionsCount));
            QuickLog(string.Format("Shift to col {0} for the next event.", "ABCDEFGHIJ".ElementAtOrDefault(curColumnIdx)));
        }
        else
        {
            QuickLog(string.Format("That wasn't right... That's a strike.", correctActionsCount));
            modSelf.HandleStrike();
            curColumnIdx = Array.IndexOf(colIDxArrangements, bombInfo.GetSerialNumberNumbers().LastOrDefault());
            QuickLog(string.Format("Go back to col {0} from the left before resuming!", "ABCDEFGHIJ".ElementAtOrDefault(curColumnIdx)));
        }
        confirmHolding = false;
        mashCount = 0;
        startInteractionTime = -1f;
        lastInteractionTime = -1f;
        noInteractionTime = -1f;
        usedIdxes.Add(rowIdxArrangements.ElementAt(affectedRowIdx));
        var possibleRowIdxArrangements = rowIdxArrangements.Except(usedIdxes).ToList();
        for (var x = 0; x < possibleRowIdxArrangements.Count; x++)
        {
            var curIdxScan = possibleRowIdxArrangements.ElementAt(x);
            if (curIdxScan % 2 == 0 && usedIdxes.Contains(curIdxScan + 1))
            {
                possibleRowIdxArrangements.Remove(curIdxScan);
                x--;
            }

        }
        if (possibleRowIdxArrangements.Any())
        {
            var curAbnormality = possibleRowIdxArrangements.PickRandom();
            RenderAbnormality(curAbnormality);
            affectedRowIdx = Array.IndexOf(rowIdxArrangements, curAbnormality);
            QuickLog(string.Format("The abnormality is now the following: {0}{1}", rendererType[curAbnormality / 2], quirkHandlingString[curAbnormality % 2]));
            var newActionRequired = correctInteractionIdxes[affectedRowIdx, curColumnIdx];
            var newTimingRequired = timedInteractionIdx[affectedRowIdx, curColumnIdx];
            QuickLog(string.Format("Using the cell at {0}{1} gives the following result: {2} {3}",
                "ABCDEFGHIJ".ElementAtOrDefault(curColumnIdx), affectedRowIdx + 1,
                newActionRequired == 0 ? "Hold" : ("Tap " + newActionRequired + " time(s)"), newTimingRequired == -1 ? "any time" : ("on last seconds digit " + newTimingRequired)));
        }
        else
        {
            QuickLog(string.Format("There were no more possible quirks left to handle. Module disarmed.", correctActionsCount));
            miscGameObjects.Last().SetActive(false);
            StartCoroutine(HandleDisarmAnim());
            modSelf.HandlePass();
            _moduleSolved = true;
        }
    }

    private int PMod(int startValue, int divisor)
    {
        return (startValue % divisor + divisor) % divisor;
    }

    private void Update()
    {
        if (_moduleSolved) return;
        var curTime = bombInfo.GetTime();
        if (startInteractionTime >= 0f)
        {
            if (noInteractionTime >= 2f)
            {
                if (isHolding && !confirmHolding)
                {
                    confirmHolding = true;
                    expectedDigitRelease = Enumerable.Range(0, 10).Concat(Enumerable.Repeat(-1, 10)).PickRandom();
                    var messUpText = UnityEngine.Random.value < 0.5f;
                    if (messUpText)
                    {
                        var releaseWordScrambled = "RELEASE".ToCharArray().Shuffle();
                        while (releaseWordScrambled.SequenceEqual("RELEASE"))
                            releaseWordScrambled.Shuffle();
                        textDisplay.text = releaseWordScrambled.Join("") + "\n" + (expectedDigitRelease == -1 ? "ANY" : "XX:X" + (expectedDigitRelease + ObtainRuleSeedDigit(rsHoldModifierDigitIdx)) % 10).ToCharArray().Shuffle().Join("");
                    }
                    else
                        textDisplay.text = "RELEASE" + (expectedDigitRelease == -1 ? "\nANY" : "\nXX:X" + expectedDigitRelease);
                    QuickLog(string.Format("When holding the button, the display says: {0}", textDisplay.text.Replace('\n', ' ')));
                    QuickLog(string.Format("You should actually release the button: {0}", expectedDigitRelease == -1 ? "any time" : ("last seconds digit " + expectedDigitRelease)));
                }
                else if (!isHolding)
                {
                    ProcessHandledAction();
                }
            }
            else
                noInteractionTime += Time.deltaTime;
        }
    }

    private IEnumerator HandleDisarmAnim()
    {
        yield return null;
        var curRenderers = possibleRenderers.ToArray().Shuffle();

        for (var x = 0; x < curRenderers.Length; x++)
        {
            if (curRenderers[x].enabled)
            { 
                if (curRenderers[x].material != null)
                {
                    curRenderers[x].material = null;
                    yield return new WaitForSeconds(0.1f);
                }
                curRenderers[x].enabled = false;
                yield return new WaitForSeconds(0.1f);
            }
        }
        var shuffledUnusedRenderers = unusedRenderers.ToArray().Shuffle();
        for (var x = 0; x < shuffledUnusedRenderers.Length; x++)
        {
            if (shuffledUnusedRenderers[x].enabled)
            {
                if (shuffledUnusedRenderers[x].material != null)
                {
                    shuffledUnusedRenderers[x].material = null;
                    yield return new WaitForSeconds(0.1f);
                }
                shuffledUnusedRenderers[x].enabled = false;
                yield return new WaitForSeconds(0.1f);
            }
        }
        textDisplay.gameObject.SetActive(false);
        var shuffledMiscGameObjects = miscGameObjects.ToArray().Shuffle();
        for (var x = 0; x < shuffledMiscGameObjects.Length; x++)
        {
            if (shuffledMiscGameObjects[x].activeSelf)
            {
                shuffledMiscGameObjects[x].SetActive(false);
                yield return new WaitForSeconds(0.1f);
            }
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

#pragma warning disable 0414
    private readonly string TwitchHelpMessage = "Tap the button that many times with \"!{0} tap #\" or once with \"!{0} tap\". Hold the button with \"!{0} hold\". Append \"on/at #\" to time the hold/taps when the last seconds digit is the specified value. NOTE: You may need to tilt the module using \"!{0} tilt\" to obtain the abnormalities.";
#pragma warning restore 0414

    private IEnumerator ProcessTwitchCommand(string command)
    {
        if (_moduleSolved)
        {
            yield return "sendtochat {0}, that module is done for. Why bother trying to interact with it?";
            yield break;
        }
        Match cmdMatchHold = Regex.Match(command, @"^h(old)?(\s((at|on)\s)?\d)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            cmdMatchTap = Regex.Match(command, @"^t(ap)?(\s\d+)?(\s((at|on)\s)?\d)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            cmdMatchRelease = Regex.Match(command, @"^release(\s((at|on)\s)?\d)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (cmdMatchHold.Success)
        {
            if (isHolding)
            {
                yield return "sendtochaterror You are currently holding a button right now, release the button first before doing so.";
                yield break;
            }
            var matchingValue = cmdMatchHold.Value;
            //QuickLog(matchingValue, false);
            var timedValueMatch = Regex.Match(matchingValue, @"(at|on)\s\d", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            yield return null;
            if (timedValueMatch.Success)
            {
                int expectedValue;
                if (int.TryParse(timedValueMatch.Value.Split().Last(), out expectedValue))
                {
                    while (expectedValue != (int)(bombInfo.GetTime() % 10))
                        yield return "trycancel Your timed hold has been canceled due to a request!";
                }
            }
            yield return ButtonSelectable;
        }
        else if (cmdMatchTap.Success)
        {
            if (isHolding)
            {
                yield return "sendtochaterror You are currently holding a button right now, release the button first before doing so.";
                yield break;
            }
            var matchingValue = cmdMatchTap.Value;
            //QuickLog(matchingValue, false);
            int tapCount = 1;
            Match timedValueMatch = Regex.Match(matchingValue, @"(at|on)\s\d", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                tapCountMatch = Regex.Match(matchingValue, @"t(ap)?\s\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (tapCountMatch.Success)
            {
                var expectedString = tapCountMatch.Value.Split().Last();
                if (!int.TryParse(expectedString, out tapCount))
                {
                    yield return string.Format("sendtochaterror I'm not tapping the button this many times: {0}", expectedString);
                    yield break;
                }
            }
            yield return null;
            if (timedValueMatch.Success)
            {
                int expectedValue;
                if (int.TryParse(timedValueMatch.Value.Split().Last(), out expectedValue))
                {
                    while (expectedValue != (int)(bombInfo.GetTime() % 10))
                        yield return "trycancel Your timed tap has been canceled due to a request!";
                }
            }
            for (var x = 0; x < tapCount; x++)
            {
                yield return ButtonSelectable;
                yield return new WaitForSeconds(0.1f);
                yield return ButtonSelectable;
                yield return new WaitForSeconds(0.1f);
            }
            yield return "strike";
            yield return "solve";
        }
        else if (cmdMatchRelease.Success)
        {
            if (!isHolding)
            {
                yield return "sendtochaterror You are currently not holding a button right now, hold the button first before doing so.";
                yield break;
            }
            if (!confirmHolding)
            {
                yield return "sendtochaterror The module has not detected that you are holding a button. Wait for a bit until sending in the command.";
                yield break;
            }
            var matchingValue = cmdMatchRelease.Value;
            //QuickLog(matchingValue, false);
            var timedValueMatch = Regex.Match(matchingValue, @"\d", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            yield return null;
            if (timedValueMatch.Success)
            {
                int expectedValue;
                if (int.TryParse(timedValueMatch.Value.Split().Last(), out expectedValue))
                {
                    while (expectedValue != (int)(bombInfo.GetTime() % 10))
                        yield return "trycancel Your timed tap has been canceled due to a request!";
                }
            }
            yield return ButtonSelectable;
        }
    }

    public IEnumerator TwitchHandleForcedSolve()
    {
        while (!_moduleSolved)
        {
            var curActionRequired = correctInteractionIdxes[affectedRowIdx, curColumnIdx];
            var curTimedAction = timedInteractionIdx[affectedRowIdx, curColumnIdx];
            while (curTimedAction != -1 && (int)(bombInfo.GetTime() % 10) != curTimedAction)
            {
                yield return true;
            }
            if (curActionRequired == 0)
            {
                ButtonSelectable.OnInteract();
                while (!confirmHolding)
                    yield return true;
                while ((int)(bombInfo.GetTime() % 10) != expectedDigitRelease && expectedDigitRelease != -1)
                    yield return true;
                ButtonSelectable.OnInteractEnded();
            }
            else
            {
                while (mashCount < curActionRequired)
                {
                    ButtonSelectable.OnInteract();
                    yield return new WaitForSeconds(0.1f);
                    ButtonSelectable.OnInteractEnded();
                    yield return new WaitForSeconds(0.1f);
                }
                while (startInteractionTime >= 0f)
                    yield return true;
            }
        }
        yield break;
    }
}
