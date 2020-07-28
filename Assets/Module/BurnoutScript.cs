using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KModkit;
using rnd = UnityEngine.Random;

public class BurnoutScript : MonoBehaviour {

	public KMBombModule _module;
	public KMBossModule _boss;
	public KMBombInfo _bomb;
	public KMAudio _audio;

	public KMSelectable _goButton;
	public KMSelectable[] _selectorButtons; // Car - Left Right, Condition - Left Right
	public KMSelectable[] _boostButtons; // 1-4

	public MeshRenderer[] _boosterMeter;

	public TextMesh[] _displayTexts; // Main, Car, Condition

	public Material[] _boostColors;

	public AudioSource _audioSource;
	public AudioClip[] _audioClips;

	// Specifically for Logging
	static int _modIDCount = 1;
	int _modID;
	private bool _modSolved;

	// 

	string[] _shortTrackNames = new string[] { "AT3", "AT1/2", "INTL", "88INT", "PBH", "SVD", "SVS", "BSG", "CSP", "CSL", "OS", "HH", "FD", "SL", "KR", "MP", "LG", "VY", "RVA", "ALP", "WINTC", "COSD", "DOCS", "GOLC", "TRODR", "BSI" };
	string[] _carNames = new string[] { "Compact", "SUV", "Coupe", "Pickup", "Sport", "Roadster", "Muscle", "Hot rod", "Cop car", "Oval racer", "Classic", "Japanese Muscle", "Gangster", "Supercar", "Custom", "Touring Car", "Buggy", "Race car", "Special", "Bike", "Future", "Drivers ED" };
	string[] _conditionNames = new string[] { "Quiet", "Crowded", "Clear", "Rainy", "Night", "Unsafe" };

	enum TrackCondition
	{
		QUIET,
		CROWDED,
		CLEAR,
		RAINY,
		NIGHT,
		UNSAFE
	}

	enum TimeCondition 
	{ 
		CARINDEX,
		FIRSTSERIAL,
		INDIPLATES,
		NONE
	}

	int _tryAmount = 1;

	int _carIndex;
	int _conditionIndex;
	int _boosts;

	string _chosenTrackName = "";
	string _chosenCarName = "";
	TrackCondition _chosenCondition;
	int _chosenBoostAmount;
	TimeCondition _chosenTimeRestraint;

	bool _trackInReverse;
	DayOfWeek _today;
	int _startingTime;

	bool[] _conditionsSet = new bool[3];
	int _curConIndex;
	int _currentCondition;
	int[] _conditionOrder = new int[3];

	bool _halfBomb, _fullBomb, _immediately = false;

	bool _started = false;
	bool _checking = false;

	bool _pressCorrect = false;
	bool _releaseCorrect = false;
	bool _firstRelease = true;
	bool _reset = false;

	int _pressIncorrectInt = 0;
	int _releaseIncorrectInt = 0;
	int _pressTime = 0;

	Coroutine reset;

	string[] _ignore = new string[] { 
		"Burnout", "14","A>N<D","Bamboozling Time Keeper","Brainf---","Busy Beaver","Cookie Jars","Divided Squares","Encrypted Hangman","Encryption Bingo","Forget Enigma","Forget Everything","Forget Infinity",
		"Forget It Not","Forget Me Later","Forget Me Not","Forget Perspective","Forget The Colors","Forget Them All","Forget This","Forget Us Not","Four-Card Monte","Hogwarts","Iconic","Kugelblitz",
		"Multitask","OmegaForget","Organization","Password Destroyer","Pressure","Purgatory","RPS Judging","Simon Forgets","Simon's Stages","Souvenir","Tallordered Keys","The Time Keeper","The Troll",
		"The Heart","The Swan","The Twin","The Very Annoying Button","Timing is Everything","Turn The Key","Ultimate Custom Night","Übermodule" };

	int _curSolved = 0;
	int _halfModules = 0;
	int _ignoredTotal = 0;

	bool _halfComplete = false;
	bool _fullComplete = false;


	void Awake() {
		_modID = _modIDCount++;

		_goButton.OnInteract = delegate () { if (_modSolved) { return false; } GoButton(); return false; };
		_goButton.OnInteractEnded = delegate () { if (_modSolved) { return; } ReleaseGoButton(); return; };

		foreach (KMSelectable km in _selectorButtons)
		{
			km.OnInteract = delegate () { if (_modSolved) { return false; } SelectConditions(km); return false; };
		}

		foreach (KMSelectable km in _boostButtons)
		{
			km.OnInteract = delegate () { if (_modSolved) { return false; } BoostMultiplier(km); return false; };
		}
	}

	void Start() {
		_displayTexts[1].text = _carNames[_carIndex];
		_displayTexts[2].text = _conditionNames[_conditionIndex];
		_today = DateTime.Now.DayOfWeek;
		_startingTime = (int)_bomb.GetTime();
		_ignore = _boss.GetIgnoredModules(_module, _ignore);
		_halfModules = _bomb.GetSolvableModuleNames().Where(x => !_ignore.Contains(x)).Count() / 2;
		_ignoredTotal = _bomb.GetSolvableModuleNames().Where(x => !_ignore.Contains(x)).Count();
		Debug.LogFormat("[Burnout #{0}]: Half of the bomb solves is at {1} and the full bomb is at {2} solves.", _modID, _halfModules, _ignoredTotal);
		GenerateAnswer();
	}

	void FixedUpdate() 
	{
		if (!_modSolved) {
			if (_bomb.GetSolvedModuleNames().Count != _curSolved) 
			{
				_curSolved = _bomb.GetSolvedModuleNames().Count();
				if (_immediately) 
				{
					_module.HandleStrike();
					Debug.LogFormat("[Burnout #{0}]: Struck due to the module wanting to be solved immediately.", _modID);
					return;
				}
				if (_halfBomb) 
				{
					if (_curSolved >= _halfModules) 
					{
						_halfComplete = true;
					}
				}
				if (_fullBomb) 
				{
					if (_curSolved == _ignoredTotal) 
					{
						_fullComplete = true;
					}
				}
			}
		}
	}

	void GoButton()
	{
		if (_checking) return;
		reset = StartCoroutine(ResetByGo(1.5f));
		if (_halfBomb && !_halfComplete) 
		{
			_module.HandleStrike();
			Debug.LogFormat("[Burnout #{0}]: Half of the bomb has yet to be completed. Strike issued.", _modID);
			return;
		}
		if (_fullBomb && !_fullComplete) 
		{
			_module.HandleStrike();
			Debug.LogFormat("[Burnout #{0}]: The bomb is not yet at its full state. Strike issued.", _modID);
			return;
		}
		if (!_started && (_carNames[_carIndex] != _chosenCarName || _conditionNames[_conditionIndex].ToUpper() != _chosenCondition.ToString() || _boosts != _chosenBoostAmount)) 
		{
			Debug.LogFormat("[Burnout #{0}]: Incorrect information given. Given {1} as the car, {2} as the condition and {3} as the boost amount, expected {4} as the car, {5} as the condition, {6} as the boost amount. Regenerating new answer...", 
				_modID, _carNames[_carIndex], _conditionNames[_conditionIndex], _boosts, _chosenCarName, _chosenCondition.ToString(), _chosenBoostAmount);
			GetComponent<KMBombModule>().HandleStrike();
			ResetModule();
			StartCoroutine(StrikeDisplay(2.5f));
			return;
		}

		MeshRenderer[] buttonRenderers = _boostButtons.Select(x => x.GetComponent<MeshRenderer>()).ToArray();

		if (!_started) 
		{
			_started = true;
			_goButton.GetComponentInChildren<TextMesh>().text = "BOOST!";
			_goButton.GetComponentInChildren<TextMesh>().characterSize = 0.0008f;

			foreach (MeshRenderer mr in buttonRenderers)
			{
				mr.material = _boostColors[2];
			}


			for (int i = 0; i <= _boosts - 1; i++)
			{
				buttonRenderers[i].material = _boostColors[3];
			}
			return;
		}

		if (_started)
		{
			int time = ((int)_bomb.GetTime()) % 60;
			int timeSum = time.ToString().Select(x => int.Parse(x.ToString())).ToList().Sum();
			_pressTime = time;
			if (_chosenTimeRestraint == TimeCondition.CARINDEX)
			{
				_pressCorrect = time.ToString().Any(x => int.Parse(x.ToString()) == _carIndex);
				_pressIncorrectInt = 0;
			}
			else if (_chosenTimeRestraint == TimeCondition.FIRSTSERIAL)
			{
				_pressCorrect = _bomb.GetSerialNumberNumbers().First() == timeSum;
				_pressIncorrectInt = 1;
			}
			else if (_chosenTimeRestraint == TimeCondition.INDIPLATES)
			{
				_pressCorrect = _bomb.GetPortPlateCount() + _bomb.GetIndicators().Count() == timeSum;
				_pressIncorrectInt = 2;
			}
			else
			{
				_pressCorrect = true;
			}

			_boosts--;

			foreach (MeshRenderer mr in buttonRenderers)
			{
				mr.material = _boostColors[2];
			}

			for (int i = 0; i < _boosts; i++)
			{
				buttonRenderers[i].material = _boostColors[3];
			}

			StartCoroutine(DelayBoostMeter(0.3f));

		}

	}

	void ReleaseGoButton()
	{
		StopCoroutine(reset);
		foreach (MeshRenderer mr in _boosterMeter)
		{
			mr.material.color = new Color32(39, 17, 17, 255);
		}
		if (_firstRelease || _reset) 
		{
			_firstRelease = false;
			_reset = false;
			return;
		}
		if (_started) 
		{

			_checking = true;
			int release = (int)_bomb.GetTime() % 60;

			if (_chosenTimeRestraint == TimeCondition.CARINDEX)
			{
				_releaseCorrect = ((int)_bomb.GetTime() % 60) % 3 == 0;
				_releaseIncorrectInt = 0;
			}
			else if (_chosenTimeRestraint == TimeCondition.FIRSTSERIAL)
			{
				_releaseCorrect = ((int)_bomb.GetTime() % 10) % 2 == 0;
				_releaseIncorrectInt = 1;
			}
			else if (_chosenTimeRestraint == TimeCondition.INDIPLATES)
			{
				_releaseCorrect = ((int)_bomb.GetTime() % 60).ToString().Select(x => int.Parse(x.ToString())).Any(x => x == _bomb.GetBatteryHolderCount());
				_releaseIncorrectInt = 2;
			}
			else
			{
				_releaseCorrect = true;
			}

			if (!_pressCorrect || !_releaseCorrect)
			{
				if (!_pressCorrect)
				{
					int c = 0;
					if (_pressIncorrectInt == 0)
					{
						c = _carIndex;
					}
					else if (_pressIncorrectInt == 1)
					{
						c = _bomb.GetSerialNumberNumbers().First();
					}
					else
					{
						c = _bomb.GetIndicators().Count() + _bomb.GetPortPlateCount();
					}
					Debug.LogFormat("[Burnout #{0}]: Incorrect press time was given. Expected a press on {1} but was given {2} (at the time pressed).", _modID, c, _pressTime);
				}
				if (!_releaseCorrect) 
				{
					string c = "";
					if (_releaseIncorrectInt == 0)
					{
						c = "the seconds digits are a multiple of 3";
					}
					else if (_releaseIncorrectInt == 1)
					{
						c = "the last second digit is a multiple of 2";
					}
					else
					{
						c = "either seconds digit is equal to the amount of battery holders";
					}
					Debug.LogFormat("[Burnout #{0}]: Incorrect release time was given. Expected a release when {1} but was given {2} (at the time released).", _modID, c, release);
				}
				GetComponent<KMBombModule>().HandleStrike();
				_audioSource.clip = _audioClips[1];
				_audioSource.Play();
				ResetModule();
				return;
			}

			if (_boosts == 0) 
			{
				GetComponent<KMBombModule>().HandlePass();
				Debug.LogFormat("[Burnout #{0}]: All boosts are successful. Module solved.", _modID); 
				_checking = false;
				_modSolved = true;
				_audioSource.clip = _audioClips[2];
				_audioSource.Play();
				StartCoroutine(StopTheSound(5f));
				return;
			}
			_audioSource.clip = _audioClips[0];
			_audioSource.Play();
			_checking = false;
		}
	}

	void SelectConditions(KMSelectable km)
	{
		int index = Array.IndexOf(_selectorButtons, km);

		switch (index)
		{
			case 0:
				if (_carIndex == 0 || _conditionsSet[1]) return;
				_carIndex--;
				if (_carNames[_carIndex] == "Japanese Muscle")
				{
					_displayTexts[1].text = "Jpn Muscle";
					return;
				}
				_displayTexts[1].text = _carNames[_carIndex];
				return;
			case 1:
				if (_carIndex == _carNames.Length - 1 || _conditionsSet[1]) return;
				_carIndex++;
				if (_carNames[_carIndex] == "Japanese Muscle")
				{
					_displayTexts[1].text = "Jpn Muscle";
					return;
				}
				_displayTexts[1].text = _carNames[_carIndex];
				return;
			case 2: 
				if (_conditionIndex == 0 || _conditionsSet[0]) return;
				_conditionIndex--;
				_displayTexts[2].text = _conditionNames[_conditionIndex];
				return;
			case 3:
				if (_conditionIndex == _conditionNames.Length - 1 || _conditionsSet[0]) return;
				_conditionIndex++;
				_displayTexts[2].text = _conditionNames[_conditionIndex];
				return;
			case 4: // Select the car
				if (_conditionsSet[1]) return;
				if (_currentCondition != 1) 
				{
					Debug.LogFormat("[Burnout #{0}]: The car isn't the current condition to be selected.", _modID);
					GetComponent<KMBombModule>().HandleStrike();
					return;
				}
				_conditionsSet[_currentCondition] = true;
				_curConIndex++;
				Debug.LogFormat("[Burnout #{0}]: The car has been set to {1}.", _modID, _carNames[_carIndex]);
				if (_curConIndex == 3) return;
				_currentCondition = _conditionOrder[_curConIndex];
				return;
			case 5: // Select the condition
				if (_conditionsSet[0]) return;
				if (_currentCondition != 0) 
				{
					Debug.LogFormat("[Burnout #{0}]: The track condition isn't the current condition to be selected.", _modID);
					GetComponent<KMBombModule>().HandleStrike();
					return;
				}
				_conditionsSet[_currentCondition] = true;
				_curConIndex++;
				Debug.LogFormat("[Burnout #{0}]: The track condition has been set to {1}.", _modID, _conditionNames[_conditionIndex].ToUpper());
				if (_curConIndex == 3) return;
				_currentCondition = _conditionOrder[_curConIndex];
				return;
			default:
				return;
		}
	}

	void BoostMultiplier(KMSelectable km)
	{
		int index = Array.IndexOf(_boostButtons, km);

		if (_conditionsSet[2]) return;
		if (_currentCondition != 2) 
		{
			Debug.LogFormat("[Burnout #{0}]: The boost amount isn't the current condition to be selected.", _modID);
			GetComponent<KMBombModule>().HandleStrike();
			return;
		}

		MeshRenderer[] buttonRenderers = _boostButtons.Select(x => x.GetComponent<MeshRenderer>()).ToArray();

		foreach (MeshRenderer mr in buttonRenderers) 
		{
			mr.material = _boostColors[0];
		}


		for (int i = 0; i <= index; i++) 
		{
			buttonRenderers[i].material = _boostColors[1];
		}

		_boosts = index + 1;

		_conditionsSet[_currentCondition] = true;
		_curConIndex++;
		Debug.LogFormat("[Burnout #{0}]: The boost amount has been set to {1}.", _modID, _boosts);
		if (_curConIndex == 3) return;
		_currentCondition = _conditionOrder[_curConIndex];

	}

	void GenerateAnswer()
	{

		// Getting a random track

		_chosenTrackName = _shortTrackNames[rnd.Range(0, _shortTrackNames.Length)];

		// Getting the Vehicle

		int carIndex = (_bomb.GetSerialNumberNumbers().Last() + _bomb.GetBatteryCount()) * (DoesSerialContain("AEIOU".ToCharArray()) ? _bomb.GetPortPlateCount() : _bomb.GetBatteryHolderCount());

		_chosenCarName = _carNames[carIndex % 22];

		// Getting the Direction of the Track

		if (_bomb.GetSerialNumberNumbers().Last() % 2 == 1) // Last digit is odd
		{
			if (_bomb.GetPorts().Distinct().Count() != _bomb.GetPortCount()) // Repeated ports
			{
				if (_bomb.GetBatteryCount() % 2 == 0) // Batteries Even
				{
					_trackInReverse = true;
				}
				else // Otherwise
				{
					_trackInReverse = false;
				}
			}
			else // Otherwise
			{
				if (_bomb.GetOnIndicators().Count() > _bomb.GetOffIndicators().Count()) // Lit Indicators > Unlit Indicators
				{
					_trackInReverse = false;
				}
				else // Otherwise
				{
					_trackInReverse = true;
				}
			}
		}
		else // Otherwise
		{
			if (_bomb.GetPorts().Distinct().Count() != _bomb.GetPortCount()) // Repeated Ports
			{
				if (_bomb.GetBatteryCount() % 2 == 0) // Batteries Even
				{
					_trackInReverse = false;
				}
				else // Otherwise
				{
					_trackInReverse = true;
				}
			}
			else // Otherwise
			{
				if (_bomb.GetOnIndicators().Count() % 2 == 0) // Even amount of lit Indicators
				{
					_trackInReverse = true;
				}
				else
				{
					_trackInReverse = false;
				}
			}
		}

		// Getting the Condition of the track

		Debug.LogFormat("[Burnout #{0}]: On try #{1}, the information is: ", _modID, _tryAmount);

		if (DoesSerialContain("D3ST7".ToCharArray()))
		{
			Debug.LogFormat("[Burnout #{0}]: The serial number contains a character from 'D3ST7', the track condition is 'UNSAFE'.", _modID);
			_chosenCondition = TrackCondition.UNSAFE;
		}
		else
		{
			_chosenCondition = GetTrackCondition();
		}

		// Getting the Boost Multiplier

		if (_bomb.GetBatteryCount() > _bomb.GetIndicators().Count())
		{
			_chosenBoostAmount = 2;
		}
		else if (_bomb.GetOnIndicators().Any(x => x == "FRK"))
		{
			_chosenBoostAmount = 4;
		}
		else if (_chosenCondition == TrackCondition.RAINY)
		{
			_chosenBoostAmount = 1;
		}
		else 
		{
			_chosenBoostAmount = 3;
		}

		// Getting the Time
		if (Array.IndexOf(_carNames, _chosenCarName).EqualsAny(0, 1, 2, 3, 4, 5, 6))
		{
			_chosenTimeRestraint = TimeCondition.CARINDEX;
		}
		else if (Array.IndexOf(_carNames, _chosenCarName).EqualsAny(7, 8, 9, 10, 11, 12, 13))
		{
			_chosenTimeRestraint = TimeCondition.FIRSTSERIAL;
		}
		else 
		{
			_chosenTimeRestraint = TimeCondition.INDIPLATES;
		}

		

		// Checking for overrides

		if ((_bomb.GetPortPlateCount() == 0 || _bomb.GetPortPlates().All(x => x.Length != 0)) && _bomb.GetOffIndicators().Count() == 0 && PortIsPresent(Port.PS2) && DoesSerialContain("C4TE6".Select(x => x).ToArray()))
		{
			Debug.LogFormat("[Burnout #{0}]: No empty port plates, no unlit indicators, PS/2 port is present and serial contains a character from 'C4TE6'. Overriding car to 'Drivers ED', the track condition to 'UNSAFE' and the time condition to 'NONE'.", _modID);
			_chosenCarName = "Drivers ED";
			_chosenCondition = TrackCondition.UNSAFE;
			_chosenTimeRestraint = TimeCondition.NONE;
		}
		if (ModuleIdIsPresent("NeedyBeer") && _bomb.GetBatteryHolderCount() >= 4) 
		{
			Debug.LogFormat("[Burnout #{0}]: 'Refill that Beer!' needy is present as well as 4 battery holders. Overriding car to 'Pickup', boost amount to '1' and time condition to 'NONE'.", _modID);
			_chosenCarName = "Pickup";
			_chosenBoostAmount = 1;
			_chosenTimeRestraint = TimeCondition.NONE;
			if (Array.IndexOf(_carNames, _chosenCarName).EqualsAny(0, 1, 2, 3, 4, 5, 6))
			{
				_chosenTimeRestraint = TimeCondition.CARINDEX;
			}
			else if (Array.IndexOf(_carNames, _chosenCarName).EqualsAny(7, 8, 9, 10, 11, 12, 13))
			{
				_chosenTimeRestraint = TimeCondition.FIRSTSERIAL;
			}
			else
			{
				_chosenTimeRestraint = TimeCondition.INDIPLATES;
			}
		}
		if (_trackInReverse && _chosenCarName == "Touring Car") 
		{
			
			if (ModuleIdIsPresent("FlagsModule") || ModuleIdIsPresent("needyFlagIdentification"))
			{
				Debug.LogFormat("[Burnout #{0}]: Module wants to be solved at the end of the bomb.", _modID);
				_fullBomb = true;
			}
			else
			{
				Debug.LogFormat("[Burnout #{0}]: Module wants to be solved for over half solves.", _modID);
				_halfBomb = true;
			}
		}
		if (_chosenCarName == "Race car" && _bomb.GetBatteryCount() == 3) 
		{
			_immediately = true;
			Debug.LogFormat("[Burnout #{0}]: Module wants to be solved immediately.", _modID);
		}

		// Selecting the condition order

		if (_chosenCarName.EqualsAny("Super", "Custom") || _trackInReverse)
		{
			_conditionOrder = new int[] { 2, 1, 0 };
		}
		else if (_chosenCarName.EqualsAny("Compact", "Cop car"))
		{
			_conditionOrder = new int[] { 2, 0, 1 };
		}
		else
		{
			_conditionOrder = new int[] { 0, 1, 2 };
		}

		_currentCondition = _conditionOrder[0];

		Debug.LogFormat("[Burnout #{0}]: The chosen track, car name and condition is: {1}, {2} and {3}, respectively.", _modID, _chosenTrackName, _chosenCarName, _chosenCondition.ToString());
		Debug.LogFormat("[Burnout #{0}]: The direction of the track is {1}.", _modID, _trackInReverse ? "in reverse" : "normal");
		Debug.LogFormat("[Burnout #{0}]: The chosen boost amount is {1}.", _modID, _chosenBoostAmount);
		Debug.LogFormat("[Burnout #{0}]: The desired order for all conditions to be entered is {1}.", _modID,
			_conditionOrder.Select(x => new string[] { "Track Condition", "Car", "Boost Amount" }[x]).ToArray().Join(", "));
		Debug.LogFormat("[Burnout #{0}]: The chosen time to boost at is {1} and release when {2}.", _modID, 
			_chosenTimeRestraint == TimeCondition.CARINDEX ? Array.IndexOf(_carNames, _chosenCarName).ToString() : 
			_chosenTimeRestraint == TimeCondition.FIRSTSERIAL ? _bomb.GetSerialNumberNumbers().First().ToString() : 
			_chosenTimeRestraint == TimeCondition.INDIPLATES ? (_bomb.GetPortPlateCount() + _bomb.GetIndicators().Count()).ToString() :
			_chosenTimeRestraint == TimeCondition.NONE ? "none" : "none",
			_chosenTimeRestraint == TimeCondition.CARINDEX ? "the seconds digits are a multiple of 3" :
			_chosenTimeRestraint == TimeCondition.FIRSTSERIAL ? "the last second digit is a multiple of 2" :
			_chosenTimeRestraint == TimeCondition.INDIPLATES ? "either of the seconds digits is equal to the amount of battery holders" :
			_chosenTimeRestraint == TimeCondition.NONE ? "none" : "none");

		_displayTexts[0].text = _chosenTrackName;
		_tryAmount++;
	}

	TrackCondition GetTrackCondition()
	{
		switch (_chosenTrackName)
		{
			case "AT3":
				if (ModuleIsPresent("Listening"))
				{
					return TrackCondition.QUIET;
				}
				else if (_bomb.GetStrikes() > 0)
				{
					return TrackCondition.CLEAR;
				}
				else if (_bomb.GetSerialNumberLetters().Any(x => x.EqualsAny('A', 'E', 'I', 'O', 'U')))
				{
					return TrackCondition.RAINY;
				}
				else if (ModuleIsPresent("Maze"))
				{
					return TrackCondition.NIGHT;
				}
				else
				{
					return TrackCondition.CROWDED;
				}
			case "AT1/2":
				if (_chosenCarName == "Muscle")
				{
					return TrackCondition.QUIET;
				}
				else if (_chosenCarName == "Pickup")
				{
					return TrackCondition.CROWDED;
				}
				else if (ModuleIsPresent("Souvenir"))
				{
					return TrackCondition.RAINY;
				}
				else if (_chosenCarName == "Touring Car")
				{
					return TrackCondition.NIGHT;
				}
				else
				{
					return TrackCondition.CLEAR;
				}
			case "INTL":
				if (_bomb.GetOnIndicators().Any(x => x == "BOB"))
				{
					return TrackCondition.QUIET;
				}
				else if (_chosenCarName == "Cop car")
				{
					return TrackCondition.CROWDED;
				}
				else if (DoesSerialContain(new char[] { 'B', 'U', 'R', 'N', 'O', 'U', 'T' }))
				{
					return TrackCondition.RAINY;
				}
				else if (_chosenCarName == "Classic")
				{
					return TrackCondition.NIGHT;
				}
				else
				{
					return TrackCondition.CLEAR;
				}
			case "88INT":
				if (PortIsPresent(Port.DVI))
				{
					return TrackCondition.CROWDED;
				}
				else if (!PortIsPresent(Port.Parallel))
				{
					return TrackCondition.CLEAR;
				}
				else if (_bomb.GetOnIndicators().Count() == _bomb.GetOffIndicators().Count())
				{
					return TrackCondition.RAINY;
				}
				else if (_chosenCarName == "Custom")
				{
					return TrackCondition.NIGHT;
				}
				else
				{
					return TrackCondition.QUIET;
				}
			case "PBH":
				if (_bomb.GetBatteryCount() > _bomb.GetBatteryHolderCount())
				{
					return TrackCondition.QUIET;
				}
				else if (_bomb.GetBatteryCount() == 2 || _bomb.GetBatteryHolderCount() == 2)
				{
					return TrackCondition.CROWDED;
				}
				else if (ModuleIsPresent("The iPhone"))
				{
					return TrackCondition.CLEAR;
				}
				else if (DayCheck(DayOfWeek.Tuesday) || DayCheck(DayOfWeek.Wednesday) || DayCheck(DayOfWeek.Thursday))
				{
					return TrackCondition.RAINY;
				}
				else
				{
					return TrackCondition.NIGHT;
				}
			case "SVD":
				if (DoesSerialContain(new char[] { 'W', 'A', 'T', 'E', 'R' }))
				{
					return TrackCondition.CROWDED;
				}
				else if (_bomb.GetPorts().Distinct().Count() != _bomb.GetPortCount())
				{
					return TrackCondition.CLEAR;
				}
				else if (ModuleIsPresent("The Time Keeper"))
				{
					return TrackCondition.RAINY;
				}
				else if (_chosenCarName == "Gangster")
				{
					return TrackCondition.NIGHT;
				}
				else
				{
					return TrackCondition.QUIET;
				}
			case "SVS":
				if (_bomb.GetIndicators().Any(x => x == "SND"))
				{
					return TrackCondition.QUIET;
				}
				else if (_chosenCarName == "Muscle")
				{
					return TrackCondition.CROWDED;
				}
				else if (_chosenCarName == "Buggy")
				{
					return TrackCondition.CLEAR;
				}
				else if (_bomb.GetIndicators().Any(x => x == "AND"))
				{
					return TrackCondition.RAINY;
				}
				else
				{
					return TrackCondition.NIGHT;
				}
			case "BSG":
				if (ModuleIsPresent("The iPhone"))
				{
					return TrackCondition.QUIET;
				}
				else if ((int)_bomb.GetTime() < _startingTime/2)
				{
					return TrackCondition.CROWDED;
				}
				else if (ModuleIsPresent("Sea Shells"))
				{
					return TrackCondition.RAINY;
				}
				else if (_chosenCarName == "Touring Car")
				{
					return TrackCondition.NIGHT;
				}
				else
				{
					return TrackCondition.CLEAR;
				}
			case "CSP":
				if (PortIsPresent(Port.Parallel))
				{
					return TrackCondition.QUIET;
				}
				else if (_chosenCarName == "Cop car")
				{
					return TrackCondition.CROWDED;
				}
				else if (_chosenCarName == "Super")
				{
					return TrackCondition.NIGHT;
				}
				else
				{
					return TrackCondition.CLEAR;
				}
			case "CSL":
				if (PortIsPresent(Port.StereoRCA))
				{
					return TrackCondition.CROWDED;
				}
				else if (_chosenCarName == "Japanese Muscle")
				{
					return TrackCondition.CLEAR;
				}
				else if (_bomb.GetModuleNames().Count() > 21)
				{
					return TrackCondition.RAINY;
				}
				else if (_chosenCarName == "Gangster")
				{
					return TrackCondition.NIGHT;
				}
				else
				{
					return TrackCondition.QUIET;
				}
			case "OS":
				if (ModuleIsPresent("Microphone"))
				{
					return TrackCondition.QUIET;
				}
				else if (_chosenCarName == "Super")
				{
					return TrackCondition.CROWDED;
				}
				else if (_bomb.GetModuleNames().Count() != _bomb.GetSolvableModuleIDs().Count())
				{
					return TrackCondition.NIGHT;
				}
				else
				{
					return TrackCondition.CLEAR;
				}
			case "HH":
				if (ModuleIsPresent("Guitar Chords"))
				{
					return TrackCondition.QUIET;
				}
				else if (_chosenCarName == "Classsic")
				{
					return TrackCondition.CLEAR;
				}
				else if (_bomb.GetIndicators().Any(x => x == "IND"))
				{
					return TrackCondition.NIGHT;
				}
				else
				{
					return TrackCondition.CROWDED;
				}
			case "FD":
				if (_bomb.GetModuleNames().Count() > _startingTime/60)
				{
					return TrackCondition.QUIET;
				}
				else if (_bomb.GetModuleNames().Count() == 23)
				{
					return TrackCondition.CROWDED;
				}
				else if (_chosenCarName == "Gangster")
				{
					return TrackCondition.CLEAR;
				}
				else if (_bomb.GetStrikes() == 0)
				{
					return TrackCondition.NIGHT;
				}
				else
				{
					return TrackCondition.RAINY;
				}
			case "SL":
				if (DoesSerialContain("F1S4".Select(x => x).ToArray()))
				{
					return TrackCondition.QUIET;
				}
				else if (_trackInReverse)
				{
					return TrackCondition.CLEAR;
				}
				else if (_bomb.GetStrikes() > 2)
				{
					return TrackCondition.RAINY;
				}
				else if (ModuleIsPresent("Mafia"))
				{
					return TrackCondition.NIGHT;
				}
				else
				{
					return TrackCondition.CROWDED;
				}
			case "KR":
				if (_chosenCarName == "Custom")
				{
					return TrackCondition.QUIET;
				}
				else if (_chosenCarName == "Drivers ED")
				{
					return TrackCondition.CROWDED;
				}
				else if (_chosenCarName == "Race car")
				{
					return TrackCondition.CLEAR;
				}
				else if (_chosenCarName == "Gangster")
				{
					return TrackCondition.NIGHT;
				}
				else
				{
					return TrackCondition.RAINY;
				}
			case "MP":
				if (ModuleIsPresent("Waste Management"))
				{
					return TrackCondition.QUIET;
				}
				else if (_chosenCarName == "Special")
				{
					return TrackCondition.CROWDED;
				}
				else if (!_trackInReverse)
				{
					return TrackCondition.NIGHT;
				}
				else
				{
					return TrackCondition.CLEAR;
				}
			case "LG":
				if (ModuleIsPresent("The Swan"))
				{
					return TrackCondition.QUIET;
				}
				else if (_chosenCarName == "Coupe")
				{
					return TrackCondition.CROWDED;
				}
				else if (ModuleIsPresent("Forget Me Not"))
				{
					return TrackCondition.CLEAR;
				}
				else if (_chosenCarName == "Bike")
				{
					return TrackCondition.NIGHT;
				}
				else
				{
					return TrackCondition.RAINY;
				}
			case "VY":
				if (_bomb.GetSerialNumberNumbers().Count() == 3)
				{
					return TrackCondition.QUIET;
				}
				else if (ModuleIsPresent("The Radio"))
				{
					return TrackCondition.CLEAR;
				}
				else if (ModuleIsPresent("Stopwatch"))
				{
					return TrackCondition.RAINY;
				}
				else if (_bomb.GetPortCount() == _bomb.GetPortPlateCount())
				{
					return TrackCondition.NIGHT;
				}
				else
				{
					return TrackCondition.CROWDED;
				}
			case "RVA":
				if (_bomb.GetPortPlateCount() < 2)
				{
					return TrackCondition.CROWDED;
				}
				else if (PortIsPresent(Port.StereoRCA))
				{
					return TrackCondition.CLEAR;
				}
				else if (ModuleIsPresent("Bomb Diffusal"))
				{
					return TrackCondition.RAINY;
				}
				else if (_bomb.GetModuleNames().Count() != _bomb.GetSolvableModuleIDs().Count())
				{
					return TrackCondition.NIGHT;
				}
				else
				{
					return TrackCondition.QUIET;
				}
			case "ALP":
				if (_bomb.GetPortCount() < 2)
				{
					return TrackCondition.QUIET;
				}
				else if (ModuleIsPresent("The Samsung"))
				{
					return TrackCondition.CROWDED;
				}
				else if (DoesSerialContain(_chosenTrackName.Select(x => x).ToArray()))
				{
					return TrackCondition.CLEAR;
				}
				else if (_bomb.GetModuleIDs().Any(x => x == "RockPaperScissorsLizardSpockModule"))
				{
					return TrackCondition.RAINY;
				}
				else
				{
					return TrackCondition.NIGHT;
				}
			case "WINTC":
				if (ModuleIsPresent("Lunchtime"))
				{
					return TrackCondition.QUIET;
				}
				else if (ModuleIsPresent("Radiator"))
				{
					return TrackCondition.CROWDED;
				}
				else if (_chosenCarName == "Bike")
				{
					return TrackCondition.NIGHT;
				}
				else
				{
					return TrackCondition.CLEAR;
				}
			case "COSD":
				if (_bomb.GetPortCount(Port.StereoRCA) > 2)
				{
					return TrackCondition.QUIET;
				}
				else if (_chosenCarName == "Roadster")
				{
					return TrackCondition.CROWDED;
				}
				else if (_chosenCarName == "Roadster")
				{
					return TrackCondition.CLEAR;
				}
				else if (ModuleIsPresent("Alliances"))
				{
					return TrackCondition.NIGHT;
				}
				else
				{
					return TrackCondition.RAINY;
				}
			case "DOCS":
				if (_chosenCarName == "Pickup")
				{
					return TrackCondition.QUIET;
				}
				else if (_bomb.GetSolvedModuleNames().Count() == _bomb.GetBatteryCount())
				{
					return TrackCondition.CROWDED;
				}
				else if (_bomb.GetSolvableModuleNames().Count() - _bomb.GetSolvedModuleNames().Count() > _bomb.GetSolvedModuleNames().Count())
				{
					return TrackCondition.CLEAR;
				}
				else if (ModuleIsPresent("Round Keypad"))
				{
					return TrackCondition.RAINY;
				}
				else
				{
					return TrackCondition.NIGHT;
				}
			case "GOLC":
				if (ModuleIsPresent("The Samsung"))
				{
					return TrackCondition.QUIET;
				}
				else if (PortIsPresent(Port.PS2))
				{
					return TrackCondition.CROWDED;
				}
				else if (_chosenCarName == "Race Car")
				{
					return TrackCondition.RAINY;
				}
				else if (_bomb.GetStrikes() > 0)
				{
					return TrackCondition.NIGHT;
				}
				else
				{
					return TrackCondition.CLEAR;
				}
			case "TRODR":
				if (_chosenCarName == "Coupe")
				{
					return TrackCondition.QUIET;
				}
				else if (_chosenCarName == "Pickup")
				{
					return TrackCondition.CLEAR;
				}
				else if (_bomb.GetIndicators().Any(x => x == "FRQ"))
				{
					return TrackCondition.RAINY;
				}
				else if (ModuleIsPresent("Wires"))
				{
					return TrackCondition.NIGHT;
				}
				else
				{
					return TrackCondition.CROWDED;
				}
			case "BSI":
				if (_chosenCarName == "Buggy")
				{
					return TrackCondition.QUIET;
				}
				else if (_bomb.GetIndicators().Any(x => x == "MSA"))
				{
					return TrackCondition.CROWDED;
				}
				else if (ModuleIsPresent("Lightspeed"))
				{
					return TrackCondition.NIGHT;
				}
				else
				{
					return TrackCondition.CLEAR;
				}
			default:
				return TrackCondition.UNSAFE;
		}
	}

	bool ModuleIsPresent(string moduleName)
	{
		return _bomb.GetModuleNames().Any(x => x == moduleName);
	}

	bool ModuleIdIsPresent(string moduleId)
	{
		return _bomb.GetModuleIDs().Any(x => x == moduleId);
	}

	bool DoesSerialContain(char[] characters) 
	{
		return _bomb.GetSerialNumber().Any(x => characters.Any(y => y == x));
	}

	bool PortIsPresent(Port port) 
	{
		return _bomb.GetPortCount(port) > 0;
	}

	bool DayCheck(DayOfWeek day) 
	{
		return _today == day;
	}

	void ResetModule()
	{
		_pressCorrect = false;
		_releaseCorrect = false;
		_firstRelease = true;
		_carIndex = 0;
		_conditionIndex = 0;
		_boosts = 0;
		_displayTexts[0].text = "";
		_displayTexts[1].text = _carNames[_carIndex];
		_displayTexts[2].text = _conditionNames[_conditionIndex];
		_conditionsSet = new bool[3];
		_currentCondition = -1;
		_curConIndex = 0;
		MeshRenderer[] buttonRenderers = _boostButtons.Select(x => x.GetComponent<MeshRenderer>()).ToArray();
		foreach (MeshRenderer mr in buttonRenderers)
		{
			mr.material = _boostColors[0];
		}
		foreach (MeshRenderer mr in _boosterMeter)
		{
			mr.material.color = new Color32(39, 17, 17, 255);
		}
		_goButton.GetComponentInChildren<TextMesh>().text = "GO!";
		_goButton.GetComponentInChildren<TextMesh>().characterSize = 0.001f;
		GenerateAnswer();
	}

	IEnumerator DelayBoostMeter(float delay) 
	{

		
		Color32[] colors = new Color32[]
		{
			new Color32(152, 9, 9, 255),
			new Color32(209, 212, 0, 255),
			new Color32(47, 178, 30, 255)
		};

		for (int i = 0; i <= 2; i++) 
		{
			_boosterMeter[i].material.color = colors[i];
			yield return new WaitForSeconds(delay);
		}
		yield break;
	}

	IEnumerator StrikeDisplay(float delay) 
	{
		_checking = true;
		if (_carNames[_carIndex] != _chosenCarName || !_conditionsSet[1]) 
		{
			_selectorButtons[4].GetComponent<MeshRenderer>().material.color = new Color32(150, 0, 0, 255);
		}
		if (_conditionNames[_conditionIndex].ToUpper() != _chosenCondition.ToString() || !_conditionsSet[0]) 
		{
			_selectorButtons[5].GetComponent<MeshRenderer>().material.color = new Color32(150, 0, 0, 255);
		}
		if (_boosts != _chosenBoostAmount || !_conditionsSet[2]) 
		{
			foreach (MeshRenderer mr in _boostButtons.Select(x => x.GetComponent<MeshRenderer>()))
			{
				mr.material = _boostColors[2];
			}
		}
		yield return new WaitForSeconds(delay);
		_selectorButtons[4].GetComponent<MeshRenderer>().material.color = new Color32(0, 0, 0, 255);
		_selectorButtons[5].GetComponent<MeshRenderer>().material.color = new Color32(0, 0, 0, 255);
		foreach (MeshRenderer mr in _boostButtons.Select(x => x.GetComponent<MeshRenderer>()))
		{
			mr.material = _boostColors[0];
		}
		_checking = false;
		yield break;
	}

	IEnumerator ResetByGo(float time) 
	{
		yield return new WaitForSeconds(time);
		if (_started) yield break;
		ResetModule();
		_reset = true;
		yield break;
	}

	IEnumerator StopTheSound(float delay) 
	{
		yield return new WaitForSeconds(delay);
		_audioSource.Stop();
		yield break;
	}

}
