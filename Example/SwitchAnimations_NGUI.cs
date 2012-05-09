// Animation selector example script
// SwitchAnimations_NGUI.cs
// Spriter Data API - Unity
//  
// Authors:
//       Josh Montoute <josh@thinksquirrel.com>
//
// 
// Copyright (c) 2012 Thinksquirrel Software, LLC
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this 
// software and associated documentation files (the "Software"), to deal in the Software 
// without restriction, including without limitation the rights to use, copy, modify, 
// merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit 
// persons to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or 
// substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT 
// NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. 
// IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, 
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE 
// SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
// NGUI is (c) by Tasharen Entertainment. Spriter is (c) by BrashMonkey.
//
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class SwitchAnimations_NGUI : MonoBehaviour {
	
	private Animation[] animations;
	private List<string[]> clips = new List<string[]>();
	private int[] selected;
	
	void Awake()
	{
		animations = FindObjectsOfType(typeof(Animation)) as Animation[];
		selected = new int[animations.Length];
		
		for(int i = 0; i < animations.Length; i++)
		{
			List<string> clipList = new List<string>();
			
			foreach(AnimationState state in animations[i])
			{
				clipList.Add(state.clip.name);
			}
			
			clips.Add(clipList.ToArray());
			
		}
	}
	
	public void OnGUI()
	{
		GUILayout.BeginArea(new Rect(0, 0, Screen.width, Screen.height));
		GUILayout.BeginHorizontal(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
		GUILayout.FlexibleSpace();
		GUILayout.BeginVertical(GUILayout.Width(150), GUILayout.ExpandHeight(true));
		GUILayout.FlexibleSpace();
		GUILayout.Space(300);
		
		GUILayout.BeginHorizontal();
		for(int i = 0; i < clips.Count; i++)
		{
			int sel = GUILayout.SelectionGrid(selected[i], clips[i], 1);
				
			if (sel != selected[i])
			{
				selected[i] = sel;
				animations[i].Stop();
				animations[i].clip = animations[i].GetClip(clips[i][sel]);
				animations[i].Play();
			}
		}
		GUILayout.EndHorizontal();
		
		Time.timeScale = GUILayout.HorizontalSlider(Time.timeScale, .5f, 1.5f);
		
		GUILayout.FlexibleSpace();
		GUILayout.EndVertical();
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.EndArea();
	}
}
