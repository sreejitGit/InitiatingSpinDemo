using System;
using Random = System.Random;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using System.Collections;
using System.IO;

[System.Serializable]
public class GameState
{
    public int saveVersion = 1;
    public LayoutState layoutState = new LayoutState();

    public void SetEmptyLayoutState()
    {
        this.layoutState = new LayoutState();
        this.layoutState.solved = false;
    }

    [System.Serializable]
    public class LayoutState
    {
        public string layoutID;
        public bool solved = false;
        public int score;
        public int matches;
        public int tries;
        public List<CardState> cardsState = new List<CardState>();

        public LayoutState Clone()
        {
            LayoutState x = new LayoutState();
            x.layoutID = layoutID;
            x.solved = solved;
            x.score = score;
            x.matches = matches;
            x.tries = tries;
            foreach (var y in cardsState)
            {
                x.cardsState.Add(y.Clone());
            }

            return x;
        }
    }
   
    [System.Serializable]
    public class CardState
    {
        public bool solved = false;
        public string spriteName;
        public bool isOpen = false;

        public CardState Clone()
        {
            CardState x = new CardState();
            x.solved = solved;
            x.spriteName = spriteName;
            x.isOpen = isOpen;
            return x;
        }
    }

    public GameState Clone()
    {
        GameState x = new GameState();
        x.layoutState = layoutState.Clone();
        return x;
    }
}

public class GameManager : MonoBehaviour
{
    [SerializeField] bool showLogs = true;
    [Header("OngoingGameState")]
    [SerializeField]  GameState ongoingGameState;

    [Header("Layout stuff")]
    [SerializeField] LayoutSpawner layoutSpawner;
    [SerializeField] List<LayoutSO> levelsLayoutSO = new List<LayoutSO>();
    LayoutSO ongoingLayoutSO;

    public UIManager UIMan => UIManager.Instance;

    [Header("gameplay stuff")]
    [SerializeField] List<Card> clickedSequenceOfCards = new List<Card>();
    [SerializeField] int currentScore = 0;
    [SerializeField] int currentMatches = 0;
    [SerializeField] int currentTries = 0;
    const string nameOfSavedFile = "InitSpinSavedData.json";
    bool levelStarted = false;
    int ongoingStreak = 1;

    private void OnEnable()
    {
        GameEvents.OnEnterHome += EnterHome;
        GameEvents.OnEnterGame += EnterGame;
        GameEvents.OnCheckForLevelCompletion += CheckForLevelCompletion;
        GameEvents.OnLayoutReady += LayoutSpawned;
        GameEvents.OnPlayerClickedShownCard += PlayerClickedShownCard;
        GameEvents.OnPlayerClickedHiddenCard += PlayerClickedHiddenCard;
        GameEvents.OnCardFlipFinished += CardFlipFinished;
    }

    private void OnDisable()
    {
        GameEvents.OnEnterHome -= EnterHome;
        GameEvents.OnEnterGame -= EnterGame;
        GameEvents.OnCheckForLevelCompletion -= CheckForLevelCompletion;
        GameEvents.OnLayoutReady -= LayoutSpawned;
        GameEvents.OnPlayerClickedShownCard -= PlayerClickedShownCard;
        GameEvents.OnPlayerClickedHiddenCard -= PlayerClickedHiddenCard;
        GameEvents.OnCardFlipFinished -= CardFlipFinished;
    }

    void Awake()
    {
        ongoingGameState = LoadSavedData();
        Application.targetFrameRate = 60;
    }

    void Start()
    {
        EnterHome();
    }

    void EnterHome()
    {
        ResetGame();
        UIMan.ChangeScreen(ScreenName.MainMenu);
    }

    void ResetGame()
    {
        ResetOngoingStreak();
        ongoingLayoutSO = null;
        currentMatches = 0;
        currentScore = 0;
        currentTries = 0;
        GameEvents.ResetGameplay();
    }

    void ResetOngoingStreak()
    {
        ongoingStreak = 1;
    }

    void EnterGame()
    {
        ResetGame();
        ongoingGameState = LoadSavedData();
        UIMan.ChangeScreen(ScreenName.Gameplay);
        SetCurrentScore(currentScore);
        SetCurrentMatches(currentMatches);
        SetCurrentTries(currentTries);
        bool foundLayoutFromLastGame = false;
        if (ongoingGameState.layoutState.solved == false)
        {
            foreach (var x in levelsLayoutSO)
            {
                if (x.layoutID == ongoingGameState.layoutState.layoutID)
                {
                    ongoingLayoutSO = x;
                    SetCurrentScore(ongoingGameState.layoutState.score);
                    SetCurrentMatches(ongoingGameState.layoutState.matches);
                    SetCurrentTries(ongoingGameState.layoutState.tries);
                    foundLayoutFromLastGame = true;
                    break;
                }
            }
        }
        if (foundLayoutFromLastGame == false)
        {
            LoadRandomLayout();
            SaveGameState();
        }
        Spawn();
    }

    void LoadRandomLayout()
    {
        if (levelsLayoutSO == null || levelsLayoutSO.Count == 0)
        {
            Debug.LogError("levelsLayoutSO is empty. Cannot load a random layout.");
            ongoingLayoutSO = null;
            ongoingGameState.SetEmptyLayoutState();
            return;
        }

        // pick from layouts that differ from the current one.
        string currentLayoutId = ongoingLayoutSO != null ? ongoingLayoutSO.layoutID : null;

        List<LayoutSO> candidates = new List<LayoutSO>();
        foreach (var layout in levelsLayoutSO)
        {
            if (layout == null)
            {
                continue;
            }
            if (!string.IsNullOrEmpty(currentLayoutId) && layout.layoutID == currentLayoutId)
            {
                continue;
            }
            candidates.Add(layout);
        }

        if (candidates.Count > 0)
        {
            ongoingLayoutSO = candidates[UnityEngine.Random.Range(0, candidates.Count)];
                }
        else
        {
            // No alternative layoutID exists Fall back safely.
            ongoingLayoutSO = ongoingLayoutSO != null ? ongoingLayoutSO : levelsLayoutSO[0];
        }
        ongoingGameState.SetEmptyLayoutState();
        ongoingGameState.layoutState.layoutID = ongoingLayoutSO.layoutID;
    }

    void Spawn()
    {
        levelStarted = false;
        clickedSequenceOfCards.Clear();
        layoutSpawner.SpawnLayout(ongoingLayoutSO);
    }

    void LayoutSpawned()
    {
        SFXManager.Instance.StopSFX(SFXManager.GameplaySFXType.LayoutOpen);
        foreach (var x in layoutSpawner.InsLayoutHorizontals)
        {
            foreach (var y in x.InsCards)
            {
                y.ToggleAllowClick(true);
            }
        }
        if (ongoingGameState.layoutState?.layoutID == ongoingLayoutSO.layoutID)
        {
            //reload from saved state.
            if (ongoingGameState.layoutState?.cardsState.Count > 0)
            {
                bool cardsOpened = false;
                if (CheckIfSavedStateIsSameLayout())
                {
                    int index = 0;
                    foreach (var x in layoutSpawner.InsLayoutHorizontals)
                    {
                        foreach (var y in x.InsCards)
                        {
                            if (ongoingGameState.layoutState.cardsState[index].solved)
                            {
                                cardsOpened = true;
                                y.SetAsSolvedFromSavedState();
                            }
                            else if (ongoingGameState.layoutState.cardsState[index].isOpen)
                            {
                                cardsOpened = true;
                                y.SetAsOpenFromSavedState();
                                clickedSequenceOfCards.Add(y);
                            }
                            index++;
                        }
                    }
                }
                else
                {
                    ongoingGameState.SetEmptyLayoutState();
                    ongoingGameState.layoutState.layoutID = ongoingLayoutSO.layoutID;
                }

                if (cardsOpened)
                {
                    SFXManager.Instance.PlaySFX(SFXManager.GameplaySFXType.CardOpen);
                }


                bool CheckIfSavedStateIsSameLayout()
                {
                    int index = 0;
                    foreach (var x in layoutSpawner.InsLayoutHorizontals)
                    {
                        foreach (var y in x.InsCards)
                        {
                            if (index >= ongoingGameState.layoutState.cardsState.Count)
                            {
                                DebugLog("index bounds layout saved state for index " + index + " name " + y.CardSprite.name);
                                return false;
                            }
                            else
                            {
                                if (y.CardSprite.name != ongoingGameState.layoutState.cardsState[index].spriteName)
                                {
                                    DebugLog("incorrect layout saved state for index " + index + " name " + y.CardSprite.name);
                                    return false;
                                }
                            }
                            index++;
                        }
                    }
                    return true;
                }
            }

        }
        SaveGameState();
        levelStarted = true;
    }

    void PlayerClickedShownCard(Card c)
    {
        SaveGameState();
    }

    void PlayerClickedHiddenCard(Card c)
    {
        if (clickedSequenceOfCards.Contains(c))
        {
            DebugLog(c.transform.name + " already in clickedSequenceOfCards");
            return;
        }
        SFXManager.Instance.PlaySFX(SFXManager.GameplaySFXType.CardOpen);
        c.Show();

        List<Card> tempCorrectCardsSequence = new List<Card>();

        bool isIncorrectClick = true;
        foreach (var x in clickedSequenceOfCards)
        {
            if (x.CardSprite == c.CardSprite)
            {
                if (tempCorrectCardsSequence.Contains(x) == false)
                {
                    tempCorrectCardsSequence.Add(x);
                }
                if (tempCorrectCardsSequence.Contains(c) == false)
                {
                    tempCorrectCardsSequence.Add(c);
                }
                isIncorrectClick = false;
            }
        }

        if (tempCorrectCardsSequence.Count == ongoingLayoutSO.NumOfCopiesInGrid)
        {
            SetCurrentMatches(currentMatches + 1);
            SetCurrentTries(currentTries + 1);
            AddToCurrentScore(ongoingStreak * ongoingLayoutSO.ScorePerCard * tempCorrectCardsSequence.Count);
            c.CallEscapedTheGrid(tempCorrectCardsSequence, ongoingStreak * ongoingLayoutSO.ScorePerCard);
            clickedSequenceOfCards.Clear();
            ongoingStreak++;
        }
        else if (isIncorrectClick == false)
        {
            if (clickedSequenceOfCards.Contains(c) == false)
            {
                clickedSequenceOfCards.Add(c);
            }
        }
        else if (clickedSequenceOfCards.Count == 0)
        {
            clickedSequenceOfCards.Add(c);
        }
        else
        {
            SetCurrentTries(currentTries + 1);
            ResetOngoingStreak();
            List<Card> incorrectCardsSequence = new List<Card>(clickedSequenceOfCards);
            c.HideASAP(incorrectCardsSequence);

            clickedSequenceOfCards.Clear();
        }
        SaveGameState();
    }

    void CardFlipFinished(Card c)
    {
        if (c.IsSolved)
        {
            return;
        }
        if (levelStarted)
        {
            if (clickedSequenceOfCards.Contains(c) == false)
            {
                c.ToggleAllowClick(true);
            }
            SaveGameState();
        }
    }

    void CheckForLevelCompletion()
    {
        if (levelStarted == false)
        {
            return;
        }
        if (IsLevelSolved())
        {
            levelStarted = false;
            SFXManager.Instance.PlaySFXOnce(SFXManager.GameplaySFXType.GameWin);
            ongoingGameState.layoutState.solved = true;
            SaveGameState();
            if (ienumRestartLevel != null)
            {
                StopCoroutine(ienumRestartLevel);
            }
            StartCoroutine(ienumRestartLevel = RestartLevel());
            //level finished
        }
        else
        {
            SaveGameState();
        }
    }

    void SaveGameState(bool writeToDisk = true)
    {
        if (ongoingLayoutSO == null)
        {
            return;
        }

        if (ongoingGameState == null)
        {
            ongoingGameState = new GameState();
        }
        else
        {
            ongoingGameState.SetEmptyLayoutState();
        }
        ongoingGameState.layoutState.layoutID = ongoingLayoutSO.layoutID;
        if (IsLevelSolved())
        {
            ongoingGameState.layoutState.solved = true;
        }
        else
        {
            ongoingGameState.layoutState.solved = false;
        }

        ongoingGameState.layoutState.score = currentScore;
        ongoingGameState.layoutState.matches = currentMatches;
        ongoingGameState.layoutState.tries = currentTries;
        ongoingGameState.layoutState.cardsState = new List<GameState.CardState>();
        foreach (var x in layoutSpawner.InsLayoutHorizontals)
        {
            foreach (var y in x.InsCards)
            {
                GameState.CardState newCardState = new GameState.CardState();
                newCardState.solved = y.IsSolved;
                newCardState.isOpen = y.IsOpen;
                newCardState.spriteName = y.CardSprite.name;
                ongoingGameState.layoutState.cardsState.Add(newCardState);
            }
        }

        if (writeToDisk)
        {
            string path = Path.Combine(Application.persistentDataPath, nameOfSavedFile);
            ongoingGameState.saveVersion = 1;
            string json = JsonUtility.ToJson(ongoingGameState, prettyPrint: false);
            File.WriteAllText(path, json);
            FileInfo fileInfo = new FileInfo(path);
            long fileSizeInBytes = fileInfo.Length;

            // Optional: Convert to kilobytes or megabytes
            float fileSizeInKB = fileSizeInBytes / 1024f;
            float fileSizeInMB = fileSizeInKB / 1024f;

            DebugLog($"Saved file size: {fileSizeInBytes} bytes ,{fileSizeInKB} KB , {fileSizeInMB} MB");
        }
    }

    public static void ResetSavedData()
    {
        string path = Path.Combine(Application.persistentDataPath, nameOfSavedFile);
        var state = new GameState();
        state.saveVersion = 1;
        string json = JsonUtility.ToJson(state, prettyPrint: false);
        File.WriteAllText(path, json);
    }

    GameState LoadSavedData()
    {
        string path = Path.Combine(Application.persistentDataPath, nameOfSavedFile);
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    var loaded = JsonUtility.FromJson<GameState>(json);
                    if (loaded != null)
                    {
                        DebugLog(" InitSpinSavedData loaded!");
                        return loaded.Clone();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Failed to load save file, starting fresh. Error: {e.Message}");
                }
            }
        }
        else
        {
            DebugLog("There is no saved InitSpinSavedData data!");
        }
        return new GameState();
    }

    IEnumerator ienumRestartLevel;
    IEnumerator RestartLevel()
    {
        yield return new WaitForSeconds(1f);
        SetCurrentScore(0);
        SetCurrentMatches(0);
        SetCurrentTries(0);
        UIMan.ChangeScreen(ScreenName.MainMenu);
    }

    bool IsLevelSolved()
    {
        List<Card> remainingCardsToSolve = new List<Card>();
        foreach (var x in layoutSpawner.InsLayoutHorizontals)
        {
            foreach (var y in x.InsCards)
            {
                if (y.IsSolved == false)
                {
                    remainingCardsToSolve.Add(y);
                }
            }
        }
        if (remainingCardsToSolve.Count <= 1)
        {
            return true;
        }
        else
        {
            foreach (var x in remainingCardsToSolve)
            {
                List<Card> group = new List<Card>();
                group.Add(x);
                foreach (var y in remainingCardsToSolve)
                {
                    if (x.CardSprite == y.CardSprite)
                    {
                        if (group.Contains(y) == false)
                        {
                            group.Add(y);
                        }
                    }
                }
                if (group.Count >= ongoingLayoutSO.NumOfCopiesInGrid)
                {
                    return false;
                }
            }
        }
        return false;
    }

    void AddToCurrentScore(int score)
    {
        currentScore += score;
        SetCurrentScore(currentScore);
    }

    void SetCurrentScore(int score)
    {
        currentScore = score;
        GameEvents.UpdatedCurrentScore(currentScore);
    }

    void SetCurrentMatches(int matches)
    {
        currentMatches = matches;
        GameEvents.UpdatedCurrentMatches(currentMatches);
    }

    void SetCurrentTries(int tries)
    {
        currentTries = tries;
        GameEvents.UpdatedCurrentTries(currentTries);
    }

    void DebugLog(string s)
    {
        if (showLogs)
        {
            Debug.Log(s);
        }
    }
}
