using KModkit;
using System;
using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using rnd = UnityEngine.Random;

public class BurnoutScript : MonoBehaviour {

	public KMBombModule _module;
	public KMBombInfo _bomb;

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

	bool _started = false;
	bool _checking = false;

	bool _pressCorrect = false;
	bool _releaseCorrect = false;
	bool _reset = false;
	bool _holding = false;

	int _pressIncorrectInt = 0;
	int _releaseIncorrectInt = 0;
	string _pressTime = "";

	Coroutine reset;
	Coroutine booster;

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
		GenerateAnswer();
	}

	void GoButton()
	{
		if (_checking || _holding) return;
		reset = StartCoroutine(ResetByGo(1.5f));
		MeshRenderer[] buttonRenderers = _boostButtons.Select(x => x.GetComponent<MeshRenderer>()).ToArray();
		_holding = true;

		if (_started)
		{
			int time = ((int)_bomb.GetTime()) % 60;
			string sTime = time.ToString();
			if (sTime.Length == 1) 
			{
				sTime = "0" + time;
			}
			int timeSum = time.ToString().Select(x => int.Parse(x.ToString())).ToList().Sum();
			_pressTime = sTime;
			if (_chosenTimeRestraint == TimeCondition.CARINDEX)
			{
				_pressCorrect = sTime.Any(x => int.Parse(x.ToString()) == _carIndex);
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

			booster = StartCoroutine(DelayBoostMeter(0.3f));

		}

	}

	void ReleaseGoButton()
	{
		if (!_holding) return;
		StopCoroutine(reset);
		_holding = false;
		if (booster != null) 
		{
			StopCoroutine(booster);
			booster = null;
		}
		if (_reset) 
		{
			_reset = false;
			return;
		}
		if (!_started && (_carNames[_carIndex] != _chosenCarName || _conditionNames[_conditionIndex].ToUpper() != _chosenCondition.ToString() || _boosts != _chosenBoostAmount))
		{
			Debug.LogFormat("[Burnout #{0}]: Incorrect information given. Given {1} as the car, {2} as the condition and {3} as the boost amount, expected {4} as the car, {5} as the condition, {6} as the boost amount. Regenerating new answer...",
				_modID, _carNames[_carIndex], _conditionNames[_conditionIndex], _boosts, _chosenCarName, _chosenCondition.ToString(), _chosenBoostAmount);
			_module.HandleStrike();
			_checking = true;
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
			foreach (MeshRenderer mr in _boosterMeter)
			{
				mr.material.color = new Color32(39, 17, 17, 255);
			}
			_checking = true;
			int release = (int)_bomb.GetTime() % 60;
			string sRelease = release.ToString();
			if (sRelease.Length == 1) 
			{
				sRelease = "0" + release;
			}

			if (_chosenTimeRestraint == TimeCondition.CARINDEX)
			{
				_releaseCorrect = release % 3 == 0;
				_releaseIncorrectInt = 0;
			}
			else if (_chosenTimeRestraint == TimeCondition.FIRSTSERIAL)
			{
				_releaseCorrect = ((int)_bomb.GetTime() % 10) % 2 == 0;
				_releaseIncorrectInt = 1;
			}
			else if (_chosenTimeRestraint == TimeCondition.INDIPLATES)
			{
				_releaseCorrect = sRelease.Select(x => int.Parse(x.ToString())).Any(x => x == _bomb.GetBatteryHolderCount());
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
					Debug.LogFormat("[Burnout #{0}]: Incorrect release time was given. Expected a release when {1} but was given {2} (at the time released).", _modID, c, sRelease);
				}
				_module.HandleStrike();
				_audioSource.clip = _audioClips[1];
				_audioSource.Play();
				ResetModule();
				_checking = false;
				return;
			}

			if (_boosts == 0) 
			{
				_module.HandlePass();
				Debug.LogFormat("[Burnout #{0}]: All boosts are successful. Module solved.", _modID); 
				_checking = false;
				_modSolved = true;
				_audioSource.clip = _audioClips[2];
				_audioSource.Play();
				_displayTexts[0].text = "POG!";
				_displayTexts[1].text = "POG!";
				_displayTexts[2].text = "POG!";
				_goButton.GetComponentInChildren<TextMesh>().text = "POG!";
				StartCoroutine(StopTheSound(5f));
				return;
			}
			foreach (MeshRenderer mr in _boosterMeter)
			{
				mr.material.color = new Color32(39, 17, 17, 255);
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
					_module.HandleStrike();
					return;
				}
				_conditionsSet[1] = true;
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
					_module.HandleStrike();
					return;
				}
				_conditionsSet[0] = true;
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
			_module.HandleStrike();
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

		_conditionsSet[2] = true;
		_curConIndex++;
		Debug.LogFormat("[Burnout #{0}]: The boost amount has been set to {1}.", _modID, _boosts);
		if (_curConIndex == 3) return;
		_currentCondition = _conditionOrder[_curConIndex];

	}

	void GenerateAnswer()
	{

		// Getting a random track

		_chosenTrackName = _shortTrackNames[rnd.Range(0, _shortTrackNames.Length)];
		//_chosenTrackName = "VY";

		// Getting the Vehicle

		int carIndex = (_bomb.GetSerialNumberNumbers().Last() + _bomb.GetBatteryCount()) * (DoesSerialContain("AEIOU".ToCharArray()) ? _bomb.GetPortPlateCount() : _bomb.GetBatteryHolderCount());

		_chosenCarName = _carNames[carIndex % 22];

        //_chosenCarName = "Custom";

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

        //_trackInReverse = true;

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

        //_chosenCondition = TrackCondition.QUIET;

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

        //_chosenBoostAmount = 3;

        // Getting the Time Condition

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

        //_chosenTimeRestraint = TimeCondition.INDIPLATES;

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
			_chosenTimeRestraint == TimeCondition.NONE ? "whenever" : "whenever",
			_chosenTimeRestraint == TimeCondition.CARINDEX ? "the seconds digits are a multiple of 3" :
			_chosenTimeRestraint == TimeCondition.FIRSTSERIAL ? "the last second digit is a multiple of 2" :
			_chosenTimeRestraint == TimeCondition.INDIPLATES ? "either of the seconds digits is equal to the amount of battery holders" :
			_chosenTimeRestraint == TimeCondition.NONE ? "whenever" : "whenever");

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
				else if (_bomb.GetIndicators().Any(x => x == "IND"))
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
		_carIndex = 0;
		_conditionIndex = 0;
		_boosts = 0;
		_displayTexts[0].text = "";
		_displayTexts[1].text = _carNames[_carIndex];
		_displayTexts[2].text = _conditionNames[_conditionIndex];
		_conditionsSet = new bool[3];
		_currentCondition = -1;
		_curConIndex = 0;
		_started = false;
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
		ResetModule();
		yield break;
	}

	IEnumerator ResetByGo(float time) 
	{
		yield return new WaitForSeconds(time);
		if (_started) yield break;
		Debug.LogFormat("[Burnout #{0}]: Resetting module by holding go...", _modID);
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

	//twitch plays
	#pragma warning disable 414
	private readonly string TwitchHelpMessage = @"!{0} car <name> [Locks in the specified car] | !{0} track <condition> [Locks in the specified track condition] | !{0} boost <#> [Locks in the specified boost amount] | !{0} go/submit [Clicks the 'GO!' button] | !{0} hold/release (##) [Holds/releases the 'BOOST!' button (optionally when the two seconds digits of the timer are '##')] | !{0} reset [Holds the 'GO!' button for three seconds]";
	#pragma warning restore 414
	private bool ZenModeActive;
	IEnumerator ProcessTwitchCommand(string command)
	{
		if (Regex.IsMatch(command, @"^\s*reset\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
		{
			yield return null;
			if (_started)
            {
				yield return "sendtochaterror The 'GO!' button is no longer present!";
				yield break;
            }
			_goButton.OnInteract();
			while (!_reset) yield return null;
			_goButton.OnInteractEnded();
		}
		if (Regex.IsMatch(command, @"^\s*go|submit\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
		{
			yield return null;
			if (_started)
			{
				yield return "sendtochaterror The 'GO!' button is no longer present!";
				yield break;
			}
			_goButton.OnInteract();
			_goButton.OnInteractEnded();
		}
		string[] parameters = command.Split(' ');
		if (Regex.IsMatch(parameters[0], @"^\s*car\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
		{
			yield return null;
			if (parameters.Length >= 2)
			{
				string name = "";
				for (int i = 1; i < parameters.Length; i++)
					name += parameters[i] + " ";
				name = name.Trim();
				string[] names = _carNames.Select(x => x.ToUpper()).ToArray();
				if (!names.Contains(name.ToUpper()))
                {
					yield return "sendtochaterror!f The specified car name '" + name + "' is invalid!";
					yield break;
                }
				if (_conditionsSet[1] || _started)
                {
					yield return "sendtochaterror The car has already been locked in!";
					yield break;
				}
				int target = Array.IndexOf(names, name.ToUpper());
				while (target < _carIndex)
                {
					_selectorButtons[0].OnInteract();
					yield return new WaitForSeconds(.1f);
				}
				while (target > _carIndex)
				{
					_selectorButtons[1].OnInteract();
					yield return new WaitForSeconds(.1f);
				}
				_selectorButtons[4].OnInteract();
			}
			else if (parameters.Length == 1)
			{
				yield return "sendtochaterror Please specify a car name!";
			}
		}
		if (Regex.IsMatch(parameters[0], @"^\s*track\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
		{
			yield return null;
			if (parameters.Length > 2)
			{
				yield return "sendtochaterror Too many parameters!";
			}
			else if (parameters.Length == 2)
			{
				string[] names = _conditionNames.Select(x => x.ToUpper()).ToArray();
				if (!names.Contains(parameters[1].ToUpper()))
				{
					yield return "sendtochaterror!f The specified track condition '" + parameters[1] + "' is invalid!";
					yield break;
				}
				if (_conditionsSet[0] || _started)
				{
					yield return "sendtochaterror The track condition has already been locked in!";
					yield break;
				}
				int target = Array.IndexOf(names, parameters[1].ToUpper());
				while (target < _conditionIndex)
				{
					_selectorButtons[2].OnInteract();
					yield return new WaitForSeconds(.1f);
				}
				while (target > _conditionIndex)
				{
					_selectorButtons[3].OnInteract();
					yield return new WaitForSeconds(.1f);
				}
				_selectorButtons[5].OnInteract();
			}
			else if (parameters.Length == 1)
			{
				yield return "sendtochaterror Please specify a track condition!";
			}
		}
		if (Regex.IsMatch(parameters[0], @"^\s*boost\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
		{
			yield return null;
			if (parameters.Length > 2)
			{
				yield return "sendtochaterror Too many parameters!";
			}
			else if (parameters.Length == 2)
			{
				int temp = -1;
				if (!int.TryParse(parameters[1], out temp))
                {
					yield return "sendtochaterror!f The specified boost amount '" + parameters[1] + "' is invalid!";
					yield break;
				}
				if (temp < 1 || temp > 4)
				{
					yield return "sendtochaterror The specified boost amount '" + parameters[1] + "' is invalid!";
					yield break;
				}
				if (_conditionsSet[2] || _started)
				{
					yield return "sendtochaterror The boost amount has already been locked in!";
					yield break;
				}
				_boostButtons[temp - 1].OnInteract();
			}
			else if (parameters.Length == 1)
			{
				yield return "sendtochaterror Please specify a boost amount!";
			}
		}
		if (Regex.IsMatch(parameters[0], @"^\s*hold\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
		{
			yield return null;
			if (parameters.Length > 2)
			{
				yield return "sendtochaterror Too many parameters!";
			}
			else if (parameters.Length == 2)
			{
				int temp = -1;
				if (!int.TryParse(parameters[1], out temp))
				{
					yield return "sendtochaterror!f The specified seconds digits '" + parameters[1] + "' are invalid!";
					yield break;
				}
				if (temp < 0 || temp > 59 || parameters[1].Length != 2)
				{
					yield return "sendtochaterror The specified seconds digits '" + parameters[1] + "' are invalid!";
					yield break;
				}
				if (!_started)
				{
					yield return "sendtochaterror The 'BOOST!' button is not present yet!";
					yield break;
				}
				if (_holding)
				{
					yield return "sendtochaterror The 'BOOST!' button is already being held!";
					yield break;
				}
				int ct = 0;
				if (ZenModeActive)
                {
					int digits = (int)_bomb.GetTime() % 60;
					while (digits != temp)
                    {
						digits++;
						ct++;
						if (digits == 60)
							digits = 0;
                    }
                }
				else
				{
					int digits = (int)_bomb.GetTime() % 60;
					while (digits != temp)
					{
						digits--;
						ct++;
						if (digits == -1)
							digits = 59;
					}
				}
				if (ct > 15 || ct == 0) yield return "waiting music";
				while ((int)_bomb.GetTime() % 60 == temp) yield return "trycancel Halted waiting to hold the 'BOOST!' button due to a cancel request.";
				while ((int)_bomb.GetTime() % 60 != temp) yield return "trycancel Halted waiting to hold the 'BOOST!' button due to a cancel request.";
				yield return "end waiting music";
				_goButton.OnInteract();
			}
			else if (parameters.Length == 1)
			{
				if (!_started)
				{
					yield return "sendtochaterror The 'BOOST!' button is not present yet!";
					yield break;
				}
				_goButton.OnInteract();
			}
		}
		if (Regex.IsMatch(parameters[0], @"^\s*release\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
		{
			yield return null;
			if (parameters.Length > 2)
			{
				yield return "sendtochaterror Too many parameters!";
			}
			else if (parameters.Length == 2)
			{
				int temp = -1;
				if (!int.TryParse(parameters[1], out temp))
				{
					yield return "sendtochaterror!f The specified seconds digits '" + parameters[1] + "' are invalid!";
					yield break;
				}
				if (temp < 0 || temp > 59 || parameters[1].Length != 2)
				{
					yield return "sendtochaterror The specified seconds digits '" + parameters[1] + "' are invalid!";
					yield break;
				}
				if (!_started)
				{
					yield return "sendtochaterror The 'BOOST!' button is not present yet!";
					yield break;
				}
				if (!_holding)
				{
					yield return "sendtochaterror The 'BOOST!' button has not been held yet!";
					yield break;
				}
				int ct = 0;
				if (ZenModeActive)
				{
					int digits = (int)_bomb.GetTime() % 60;
					while (digits != temp)
					{
						digits++;
						ct++;
						if (digits == 60)
							digits = 0;
					}
				}
				else
				{
					int digits = (int)_bomb.GetTime() % 60;
					while (digits != temp)
					{
						digits--;
						ct++;
						if (digits == -1)
							digits = 59;
					}
				}
				if (ct > 15 || ct == 0) yield return "waiting music";
				while ((int)_bomb.GetTime() % 60 == temp) yield return "trycancel Halted waiting to release the 'BOOST!' button due to a cancel request.";
				while ((int)_bomb.GetTime() % 60 != temp) yield return "trycancel Halted waiting to release the 'BOOST!' button due to a cancel request.";
				yield return "end waiting music";
				_goButton.OnInteractEnded();
			}
			else if (parameters.Length == 1)
			{
				if (!_started)
				{
					yield return "sendtochaterror The 'BOOST!' button is not present yet!";
					yield break;
				}
				_goButton.OnInteractEnded();
			}
		}
	}

	IEnumerator TwitchHandleForcedSolve()
    {
		if (_holding && _reset)
			_goButton.OnInteractEnded();
		while (_checking) yield return true;
		if (!_started)
        {
			if ((_carNames[_carIndex] != _chosenCarName && _conditionsSet[1]) || (_conditionNames[_conditionIndex].ToUpper() != _chosenCondition.ToString() && _conditionsSet[0]) || (_boosts != _chosenBoostAmount && _conditionsSet[2]) || (_holding && (!_conditionsSet[0] || !_conditionsSet[1] || !_conditionsSet[2])))
			{
				if (!_holding)
					_goButton.OnInteract();
				while (!_reset) yield return true;
				_goButton.OnInteractEnded();
			}
			for (int i = _curConIndex; i < 3; i++)
            {
				if (_currentCondition == 1)
					yield return ProcessTwitchCommand("car " + _chosenCarName);
				if (_currentCondition == 0)
					yield return ProcessTwitchCommand("track " + _chosenCondition.ToString());
				if (_currentCondition == 2)
					yield return ProcessTwitchCommand("boost " + _chosenBoostAmount);
				yield return new WaitForSeconds(.1f);
			}
			if (!_holding)
				_goButton.OnInteract();
			_goButton.OnInteractEnded();
		}
		if (_holding && !_pressCorrect)
        {
			_module.HandlePass();
			_modSolved = true;
			_audioSource.clip = _audioClips[2];
			_audioSource.Play();
			_displayTexts[0].text = "POG!";
			_displayTexts[1].text = "POG!";
			_displayTexts[2].text = "POG!";
			_goButton.GetComponentInChildren<TextMesh>().text = "POG!";
			StartCoroutine(StopTheSound(5f));
		}
		while (!_modSolved)
        {
			if (!_holding)
            {
				reCheck:
				int time = ((int)_bomb.GetTime()) % 60;
				string sTime = time.ToString();
				if (sTime.Length == 1)
				{
					sTime = "0" + time;
				}
				bool goodTime = false;
				int timeSum = time.ToString().Select(x => int.Parse(x.ToString())).ToList().Sum();
				if (_chosenTimeRestraint == TimeCondition.CARINDEX)
					goodTime = sTime.Any(x => int.Parse(x.ToString()) == _carIndex);
				else if (_chosenTimeRestraint == TimeCondition.FIRSTSERIAL)
					goodTime = _bomb.GetSerialNumberNumbers().First() == timeSum;
				else if (_chosenTimeRestraint == TimeCondition.INDIPLATES)
					goodTime = _bomb.GetPortPlateCount() + _bomb.GetIndicators().Count() == timeSum;
				else if (_chosenTimeRestraint == TimeCondition.NONE)
					goodTime = true;
				if (!goodTime)
                {
					yield return true;
					goto reCheck;
				}
                else
                {
					_goButton.OnInteract();
					yield return new WaitForSeconds(.1f);
				}
			}
			reCheck2:
			int release = (int)_bomb.GetTime() % 60;
			string sRelease = release.ToString();
			if (sRelease.Length == 1)
			{
				sRelease = "0" + release;
			}
			bool goodTime2 = false;
			if (_chosenTimeRestraint == TimeCondition.CARINDEX)
				goodTime2 = release % 3 == 0;
			else if (_chosenTimeRestraint == TimeCondition.FIRSTSERIAL)
				goodTime2 = ((int)_bomb.GetTime() % 10) % 2 == 0;
			else if (_chosenTimeRestraint == TimeCondition.INDIPLATES)
				goodTime2 = sRelease.Select(x => int.Parse(x.ToString())).Any(x => x == _bomb.GetBatteryHolderCount());
			else if (_chosenTimeRestraint == TimeCondition.NONE)
				goodTime2 = true;
			if (!goodTime2)
			{
				yield return true;
				goto reCheck2;
			}
			else
			{
				_goButton.OnInteractEnded();
				yield return new WaitForSeconds(.1f);
			}
		}
    }

}
