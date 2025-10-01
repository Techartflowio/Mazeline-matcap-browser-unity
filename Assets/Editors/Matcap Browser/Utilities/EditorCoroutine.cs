/*
 * ============================================================================
 * EditorCoroutine - Utility
 * ============================================================================
 * 
 * Unity 에디터에서 코루틴을 실행하기 위한 헬퍼 클래스
 * 
 * ============================================================================
 */

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ML.Editor.MatcapBrowser.Utilities
{
    /// <summary>
    /// Unity 에디터에서 코루틴을 실행하기 위한 헬퍼 클래스
    /// 에디터 업데이트 루프를 사용하여 비동기 작업을 처리합니다.
    /// </summary>
    public class EditorCoroutine
    {
        private readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        private AsyncOperation waitingAsyncOp;
        private CustomYieldInstruction waitingCustomYield;
        private bool isDone;

        private EditorCoroutine(IEnumerator routine)
        {
            stack.Push(routine);
        }

        /// <summary>
        /// 에디터 코루틴을 시작합니다.
        /// </summary>
        /// <param name="routine">실행할 코루틴</param>
        /// <returns>EditorCoroutine 인스턴스</returns>
        public static EditorCoroutine Start(IEnumerator routine)
        {
            if (routine == null) throw new ArgumentNullException(nameof(routine));
            var coroutine = new EditorCoroutine(routine);
            coroutine.Start();
            return coroutine;
        }

        private void Start()
        {
            EditorApplication.update += Update;
        }

        /// <summary>
        /// 실행 중인 에디터 코루틴을 중지합니다.
        /// </summary>
        /// <param name="coroutine">중지할 코루틴</param>
        public static void Stop(EditorCoroutine coroutine)
        {
            if (coroutine != null)
            {
                coroutine.Stop();
            }
        }

        private void Stop()
        {
            isDone = true;
            waitingAsyncOp = null;
            waitingCustomYield = null;
            stack.Clear();
            EditorApplication.update -= Update;
        }

        /// <summary>
        /// 에디터 업데이트마다 호출되어 코루틴을 진행시킵니다.
        /// </summary>
        private void Update()
        {
            if (isDone) return;

            if (waitingAsyncOp != null)
            {
                if (!waitingAsyncOp.isDone) return;
                waitingAsyncOp = null;
            }

            if (waitingCustomYield != null)
            {
                if (waitingCustomYield.keepWaiting) return;
                waitingCustomYield = null;
            }

            if (stack.Count == 0)
            {
                Stop();
                return;
            }

            var enumerator = stack.Peek();
            bool movedNext = false;

            try
            {
                movedNext = enumerator.MoveNext();
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                Stop();
                return;
            }

            if (!movedNext)
            {
                stack.Pop();
                if (stack.Count == 0) Stop();
                return;
            }

            var yielded = enumerator.Current;

            if (yielded == null)
            {
                return;
            }

            if (yielded is IEnumerator nested)
            {
                stack.Push(nested);
                return;
            }

            if (yielded is AsyncOperation asyncOp)
            {
                waitingAsyncOp = asyncOp;
                return;
            }

            if (yielded is CustomYieldInstruction customYield)
            {
                waitingCustomYield = customYield;
                return;
            }
        }
    }
}

