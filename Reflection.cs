//
// Copyright (c) Roland Pihlakas 2011 - 2023
// roland@simplify.ee
//
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE file for more information.
//

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace FolderSync
{
    public static class EnumReflector
    {
        private static ConcurrentDictionary<string, Dictionary<string, int>> cache;

        public static Dictionary<string, int> FindEnum(Type enumType)
        {
            if (cache == null)
                cache = new ConcurrentDictionary<string, Dictionary<string, int>>();

            var cacheKey = enumType.FullName;

            Dictionary<string, int> dict = null;

            if (!cache.TryGetValue(cacheKey, out dict))
            {
                var values = Enum.GetValues(enumType).Cast<int>(); //.ToArray();
                var names = Enum.GetNames(enumType);

                dict = names.Zip(values, (key, val) => new { key, val }).ToDictionary(item => item.key, item => item.val);

                cache.TryAdd(cacheKey, dict);
            }

            return dict;
        }

        public static int GetValue(Type enumType, string key)
        {
            var dict = FindEnum(enumType);
            return dict[key];
        }

    }   //public static class EnumReflector<T>

    // ############################################################################
    //
    // ############################################################################

    internal static class PrivateClassMethodInvokerHelper
    {
        public static bool AreTypesCompatible(ParameterInfo parameterInfo, Type callerParamType)
        {
            return
                parameterInfo.ParameterType == callerParamType
                ||
                (
                    parameterInfo.ParameterType.IsEnum
                    && callerParamType == TypeOf<int>.Value
                );
        }
    }

    // ############################################################################
    //
    // ############################################################################
    
    public static class PrivateClassMethodInvoker_Void<T>
    {
        private static ConcurrentDictionary<string, Action<T>> cache;
        private static Type baseType = TypeOf<T>.BaseType;

        public static Action<T> FindMethod(string methodName)
        {
            if (cache == null)
                cache = new ConcurrentDictionary<string, Action<T>>();

            var key = methodName;

            MethodInfo method = null;
            Action<T> _delegate = null;

            if (!cache.TryGetValue(key, out _delegate))
            {
                var methods = baseType.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

                foreach (var info in methods)
                {
                    if (info.Name == methodName || info.Name.EndsWith("." + methodName))
                    {
                        var pars = info.GetParameters();
                        if (
                            pars.Length == 0
                        //&& pars.Count(x => !x.IsOptional) == 0
                        )
                        {
                            method = info;
                            _delegate = (Action<T>)Delegate.CreateDelegate(
                                                                    TypeOf<Action<T>>.Value, /*baseType, */method);
                            break;
                        }
                    }
                }

                cache.TryAdd(key, _delegate);
            }

            return _delegate;
        }

        public static void Invoke(T obj, string methodName)
        {
            var method = FindMethod(methodName);
            method(obj);
        }

    }   //public static class PrivateClassMethodInvoker<T>

    // ############################################################################

    public static class PrivateStaticClassMethodInvoker_Void
    {
        private static ConcurrentDictionary<string, Action> cache;

        public static Action FindMethod(Type baseType, string methodName)
        {
            if (cache == null)
                cache = new ConcurrentDictionary<string, Action>();

            var key = baseType.FullName + "." + methodName;

            MethodInfo method = null;
            Action _delegate = null;

            if (!cache.TryGetValue(key, out _delegate))
            {
                var methods = baseType.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

                foreach (var info in methods)
                {
                    if (info.Name == methodName || info.Name.EndsWith("." + methodName))
                    {
                        var pars = info.GetParameters();
                        if (
                            pars.Length == 0
                        )
                        {
                            method = info;
                            _delegate = (Action)Delegate.CreateDelegate(
                                                                    TypeOf<Action>.Value, /*baseType, */method);
                            break;
                        }
                    }
                }

                cache.TryAdd(key, _delegate);
            }

            return _delegate;
        }

        public static void Invoke(Type baseType, string methodName)
        {
            var method = FindMethod(baseType, methodName);
            method();
        }

    }   //public static class PrivateStaticClassMethodInvoker<T>

    // ############################################################################

    public static class PrivateClassMethodInvoker_Void<T, A1>
    {
        private static ConcurrentDictionary<string, Action<T, A1>> cache;
        private static Type baseType = TypeOf<T>.BaseType;

        public static Action<T, A1> FindMethod(string methodName)
        {
            if (cache == null)
                cache = new ConcurrentDictionary<string, Action<T, A1>>();

            var key = methodName;

            MethodInfo method = null;
            Action<T, A1> _delegate = null;

            if (!cache.TryGetValue(key, out _delegate))
            {
                var methods = baseType.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

                foreach (var info in methods)
                {
                    if (info.Name == methodName || info.Name.EndsWith("." + methodName))
                    {
                        var pars = info.GetParameters();
                        if (
                            pars.Length == 1
                            && PrivateClassMethodInvokerHelper.AreTypesCompatible(pars[0], TypeOf<A1>.Value)
                        )
                        {
                            method = info;
                            _delegate = (Action<T, A1>)Delegate.CreateDelegate(
                                                                    TypeOf<Action<T, A1>>.Value, /*baseType, */method);
                            break;
                        }
                    }
                }

                cache.TryAdd(key, _delegate);
            }

            return _delegate;
        }

        public static void Invoke(T obj, string methodName, A1 arg1)
        {
            var method = FindMethod(methodName);
            method(obj, arg1);
        }

    }   //public static class PrivateClassMethodInvoker<T>

    // ############################################################################

    public static class PrivateClassMethodInvoker_VoidRef<T, A1>
    {
        public delegate void VoidMethodDelegate(T obj, ref A1 arg1);

        private static ConcurrentDictionary<string, VoidMethodDelegate> cache;
        private static Type baseType = TypeOf<T>.BaseType;

        public static VoidMethodDelegate FindMethod(string methodName)
        {
            if (cache == null)
                cache = new ConcurrentDictionary<string, VoidMethodDelegate>();

            var key = methodName;

            MethodInfo method = null;
            VoidMethodDelegate _delegate = null;

            if (!cache.TryGetValue(key, out _delegate))
            {
                var methods = baseType.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

                foreach (var info in methods)
                {
                    if (info.Name == methodName || info.Name.EndsWith("." + methodName))
                    {
                        var pars = info.GetParameters();
                        if (
                            pars.Length == 1
                            && PrivateClassMethodInvokerHelper.AreTypesCompatible(pars[0], TypeOf<A1>.Value)
                        )
                        {
                            method = info;
                            _delegate = (VoidMethodDelegate)Delegate.CreateDelegate(
                                                                    TypeOf<VoidMethodDelegate>.Value, /*baseType, */method);
                            break;
                        }
                    }
                }

                cache.TryAdd(key, _delegate);
            }

            return _delegate;
        }

        public static void Invoke(T obj, string methodName, ref A1 arg1)
        {
            var method = FindMethod(methodName);
            method(obj, ref arg1);
        }

    }   //public static class PrivateClassMethodInvoker<T>

    // ############################################################################
  
    public static class PrivateStaticClassMethodInvoker_Void<A1>
    {
        private static ConcurrentDictionary<string, Action<A1>> cache;

        public static Action<A1> FindMethod(Type baseType, string methodName)
        {
            if (cache == null)
                cache = new ConcurrentDictionary<string, Action<A1>>();

            var key = baseType.FullName + "." + methodName;

            MethodInfo method = null;
            Action<A1> _delegate = null;

            if (!cache.TryGetValue(key, out _delegate))
            {
                var methods = baseType.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

                foreach (var info in methods)
                {
                    if (info.Name == methodName || info.Name.EndsWith("." + methodName))
                    {
                        var pars = info.GetParameters();
                        if (
                            pars.Length == 1
                            && PrivateClassMethodInvokerHelper.AreTypesCompatible(pars[0], TypeOf<A1>.Value)
                        )
                        {
                            method = info;
                            _delegate = (Action<A1>)Delegate.CreateDelegate(
                                                                    TypeOf<Action<A1>>.Value, /*baseType, */method);
                            break;
                        }
                    }
                }

                cache.TryAdd(key, _delegate);
            }

            return _delegate;
        }

        public static void Invoke(Type baseType, string methodName, A1 arg1)
        {
            var method = FindMethod(baseType, methodName);
            method(arg1);
        }

    }   //public static class PrivateStaticClassMethodInvoker<T>

    // ############################################################################
   
    public static class PrivateClassMethodInvoker_Void<T, A1, A2>
    {
        private static ConcurrentDictionary<string, Action<T, A1, A2>> cache;
        private static Type baseType = TypeOf<T>.BaseType;

        public static Action<T, A1, A2> FindMethod(string methodName)
        {
            if (cache == null)
                cache = new ConcurrentDictionary<string, Action<T, A1, A2>>();

            var key = methodName;

            MethodInfo method = null;
            Action<T, A1, A2> _delegate = null;

            if (!cache.TryGetValue(key, out _delegate))
            {
                var methods = baseType.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

                foreach (var info in methods)
                {
                    if (info.Name == methodName || info.Name.EndsWith("." + methodName))
                    {
                        var pars = info.GetParameters();
                        if (
                            pars.Length == 2
                            && PrivateClassMethodInvokerHelper.AreTypesCompatible(pars[0], TypeOf<A1>.Value)
                            && PrivateClassMethodInvokerHelper.AreTypesCompatible(pars[1], TypeOf<A2>.Value)
                        )
                        {
                            method = info;
                            _delegate = (Action<T, A1, A2>)Delegate.CreateDelegate(
                                                                    TypeOf<Action<T, A1, A2>>.Value, /*baseType, */method);
                            break;
                        }
                    }
                }

                cache.TryAdd(key, _delegate);
            }

            return _delegate;
        }

        public static void Invoke(T obj, string methodName, A1 arg1, A2 arg2)
        {
            var method = FindMethod(methodName);
            method(obj, arg1, arg2);
        }

    }   //public static class PrivateClassMethodInvoker<T>

    // ############################################################################
 
    public static class PrivateStaticClassMethodInvoker_Void<A1, A2>
    {
        private static ConcurrentDictionary<string, Action<A1, A2>> cache;

        public static Action<A1, A2> FindMethod(Type baseType, string methodName)
        {
            if (cache == null)
                cache = new ConcurrentDictionary<string, Action<A1, A2>>();

            var key = baseType.FullName + "." + methodName;

            MethodInfo method = null;
            Action<A1, A2> _delegate = null;

            if (!cache.TryGetValue(key, out _delegate))
            {
                var methods = baseType.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

                foreach (var info in methods)
                {
                    if (info.Name == methodName || info.Name.EndsWith("." + methodName))
                    {
                        var pars = info.GetParameters();
                        if (
                            pars.Length == 2
                            && PrivateClassMethodInvokerHelper.AreTypesCompatible(pars[0], TypeOf<A1>.Value)
                            && PrivateClassMethodInvokerHelper.AreTypesCompatible(pars[1], TypeOf<A2>.Value)
                        )
                        {
                            method = info;
                            _delegate = (Action<A1, A2>)Delegate.CreateDelegate(
                                                                    TypeOf<Action<A1, A2>>.Value, /*baseType, */method);
                            break;
                        }
                    }
                }

                cache.TryAdd(key, _delegate);
            }

            return _delegate;
        }

        public static void Invoke(Type baseType, string methodName, A1 arg1, A2 arg2)
        {
            var method = FindMethod(baseType, methodName);
            method(arg1, arg2);
        }

    }   //public static class PrivateStaticClassMethodInvoker<T>

    // ############################################################################
    //
    // ############################################################################
  
    public static class PrivateClassMethodInvoker<RT, T>
    {
        private static ConcurrentDictionary<string, Func<T, RT>> cache;
        private static Type baseType = TypeOf<T>.BaseType;

        public static Func<T, RT> FindMethod(string methodName)
        {
            if (cache == null)
                cache = new ConcurrentDictionary<string, Func<T, RT>>();

            var key = methodName;

            MethodInfo method = null;
            Func<T, RT> _delegate = null;

            if (!cache.TryGetValue(key, out _delegate))
            {
                var methods = baseType.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

                foreach (var info in methods)
                {
                    if (info.Name == methodName || info.Name.EndsWith("." + methodName))
                    {
                        var pars = info.GetParameters();
                        if (
                            pars.Length == 0
                        )
                        {
                            method = info;
                            _delegate = (Func<T, RT>)Delegate.CreateDelegate(
                                                                    TypeOf<Func<T, RT>>.Value, /*baseType, */method);
                            break;
                        }
                    }
                }

                cache.TryAdd(key, _delegate);
            }

            return _delegate;
        }

        public static RT Invoke(T obj, string methodName)
        {
            var method = FindMethod(methodName);
            return method(obj);
        }

    }   //public static class PrivateClassMethodInvoker<T>

    // ############################################################################
  
    public static class PrivateStaticClassMethodInvoker<RT>
    {
        private static ConcurrentDictionary<string, Func<RT>> cache;

        public static Func<RT> FindMethod(Type baseType, string methodName)
        {
            if (cache == null)
                cache = new ConcurrentDictionary<string, Func<RT>>();

            var key = baseType.FullName + "." + methodName;

            MethodInfo method = null;
            Func<RT> _delegate = null;

            if (!cache.TryGetValue(key, out _delegate))
            {
                var methods = baseType.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

                foreach (var info in methods)
                {
                    if (info.Name == methodName || info.Name.EndsWith("." + methodName))
                    {
                        var pars = info.GetParameters();
                        if (
                            pars.Length == 0
                        )
                        {
                            method = info;
                            _delegate = (Func<RT>)Delegate.CreateDelegate(
                                                                    TypeOf<Func<RT>>.Value, /*baseType, */method);
                            break;
                        }
                    }
                }

                cache.TryAdd(key, _delegate);
            }

            return _delegate;
        }

        public static RT Invoke(Type baseType, string methodName)
        {
            var method = FindMethod(baseType, methodName);
            return method();
        }

    }   //public static class PrivateStaticClassMethodInvoker<T>

    // ############################################################################
    
    public static class PrivateClassMethodInvoker<RT, T, A1>
    {
        private static ConcurrentDictionary<string, Func<T, A1, RT>> cache;
        private static Type baseType = TypeOf<T>.BaseType;

        public static Func<T, A1, RT> FindMethod(string methodName)
        {
            if (cache == null)
                cache = new ConcurrentDictionary<string, Func<T, A1, RT>>();

            var key = methodName;

            MethodInfo method = null;
            Func<T, A1, RT> _delegate = null;

            if (!cache.TryGetValue(key, out _delegate))
            {
                var methods = baseType.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

                foreach (var info in methods)
                {
                    if (info.Name == methodName || info.Name.EndsWith("." + methodName))
                    {
                        var pars = info.GetParameters();
                        if (
                            pars.Length == 1
                            && PrivateClassMethodInvokerHelper.AreTypesCompatible(pars[0], TypeOf<A1>.Value)
                        )
                        {
                            method = info;
                            _delegate = (Func<T, A1, RT>)Delegate.CreateDelegate(
                                                                    TypeOf<Func<T, A1, RT>>.Value, /*baseType, */method);
                            break;
                        }
                    }
                }

                cache.TryAdd(key, _delegate);
            }

            return _delegate;
        }

        public static RT Invoke(T obj, string methodName, A1 arg1)
        {
            var method = FindMethod(methodName);
            return method(obj, arg1);
        }

    }   //public static class PrivateClassMethodInvoker<T>

    // ############################################################################
    
    public static class PrivateStaticClassMethodInvoker<RT, A1>
    {
        private static ConcurrentDictionary<string, Func<A1, RT>> cache;

        public static Func<A1, RT> FindMethod(Type baseType, string methodName)
        {
            if (cache == null)
                cache = new ConcurrentDictionary<string, Func<A1, RT>>();

            var key = baseType.FullName + "." + methodName;

            MethodInfo method = null;
            Func<A1, RT> _delegate = null;

            if (!cache.TryGetValue(key, out _delegate))
            {
                var methods = baseType.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

                foreach (var info in methods)
                {
                    if (info.Name == methodName || info.Name.EndsWith("." + methodName))
                    {
                        var pars = info.GetParameters();
                        if (
                            pars.Length == 1
                            && PrivateClassMethodInvokerHelper.AreTypesCompatible(pars[0], TypeOf<A1>.Value)
                        )
                        {
                            method = info;
                            _delegate = (Func<A1, RT>)Delegate.CreateDelegate(
                                                                    TypeOf<Func<A1, RT>>.Value, /*baseType, */method);
                            break;
                        }
                    }
                }

                cache.TryAdd(key, _delegate);
            }

            return _delegate;
        }

        public static RT Invoke(Type baseType, string methodName, A1 arg1)
        {
            var method = FindMethod(baseType, methodName);
            return method(arg1);
        }

    }   //public static class PrivateStaticClassMethodInvoker<T>

    // ############################################################################
    
    public static class PrivateStaticClassMethodInvoker<RT, A1, A2>
    {
        private static ConcurrentDictionary<string, Func<A1, A2, RT>> cache;

        public static Func<A1, A2, RT> FindMethod(Type baseType, string methodName)
        {
            if (cache == null)
                cache = new ConcurrentDictionary<string, Func<A1, A2, RT>>();

            var key = baseType.FullName + "." + methodName;

            MethodInfo method = null;
            Func<A1, A2, RT> _delegate = null;

            if (!cache.TryGetValue(key, out _delegate))
            {
                var methods = baseType.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

                foreach (var info in methods)
                {
                    if (info.Name == methodName || info.Name.EndsWith("." + methodName))
                    {
                        var pars = info.GetParameters();
                        if (
                            pars.Length == 2
                            && PrivateClassMethodInvokerHelper.AreTypesCompatible(pars[0], TypeOf<A1>.Value)
                            && PrivateClassMethodInvokerHelper.AreTypesCompatible(pars[1], TypeOf<A2>.Value)
                        )
                        {
                            method = info;
                            _delegate = (Func<A1, A2, RT>)Delegate.CreateDelegate(
                                                                    TypeOf<Func<A1, A2, RT>>.Value, /*baseType, */method);
                            break;
                        }
                    }
                }

                cache.TryAdd(key, _delegate);
            }

            return _delegate;
        }

        public static RT Invoke(Type baseType, string methodName, A1 arg1, A2 arg2)
        {
            var method = FindMethod(baseType, methodName);
            return method(arg1, arg2);
        }

    }   //public static class PrivateStaticClassMethodInvoker<T>

    // ############################################################################
    
    public static class PrivateStaticClassMethodInvoker<RT, A1, A2, A3>
    {
        private static ConcurrentDictionary<string, Func<A1, A2, A3, RT>> cache;

        public static Func<A1, A2, A3, RT> FindMethod(Type baseType, string methodName)
        {
            if (cache == null)
                cache = new ConcurrentDictionary<string, Func<A1, A2, A3, RT>>();

            var key = baseType.FullName + "." + methodName;

            MethodInfo method = null;
            Func<A1, A2, A3, RT> _delegate = null;

            if (!cache.TryGetValue(key, out _delegate))
            {
                var methods = baseType.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

                foreach (var info in methods)
                {
                    if (info.Name == methodName || info.Name.EndsWith("." + methodName))
                    {
                        var pars = info.GetParameters();
                        if (
                            pars.Length == 3
                            && PrivateClassMethodInvokerHelper.AreTypesCompatible(pars[0], TypeOf<A1>.Value)
                            && PrivateClassMethodInvokerHelper.AreTypesCompatible(pars[1], TypeOf<A2>.Value)
                            && PrivateClassMethodInvokerHelper.AreTypesCompatible(pars[2], TypeOf<A3>.Value)
                        )
                        {
                            method = info;
                            _delegate = (Func<A1, A2, A3, RT>)Delegate.CreateDelegate(
                                                                    TypeOf<Func<A1, A2, A3, RT>>.Value, /*baseType, */method);
                            break;
                        }
                    }
                }

                cache.TryAdd(key, _delegate);
            }

            return _delegate;
        }

        public static RT Invoke(Type baseType, string methodName, A1 arg1, A2 arg2, A3 arg3)
        {
            var method = FindMethod(baseType, methodName);
            return method(arg1, arg2, arg3);
        }

    }   //public static class PrivateStaticClassMethodInvoker<T>

    // ############################################################################
   
    public static class PrivateStaticClassMethodInvoker<RT, A1, A2, A3, A4>
    {
        private static ConcurrentDictionary<string, Func<A1, A2, A3, A4, RT>> cache;

        public static Func<A1, A2, A3, A4, RT> FindMethod(Type baseType, string methodName)
        {
            if (cache == null)
                cache = new ConcurrentDictionary<string, Func<A1, A2, A3, A4, RT>>();

            var key = baseType.FullName + "." + methodName;

            MethodInfo method = null;
            Func<A1, A2, A3, A4, RT> _delegate = null;

            if (!cache.TryGetValue(key, out _delegate))
            {
                var methods = baseType.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

                foreach (var info in methods)
                {
                    if (info.Name == methodName || info.Name.EndsWith("." + methodName))
                    {
                        var pars = info.GetParameters();
                        if (
                            pars.Length == 4
                            && PrivateClassMethodInvokerHelper.AreTypesCompatible(pars[0], TypeOf<A1>.Value)
                            && PrivateClassMethodInvokerHelper.AreTypesCompatible(pars[1], TypeOf<A2>.Value)
                            && PrivateClassMethodInvokerHelper.AreTypesCompatible(pars[2], TypeOf<A3>.Value)
                            && PrivateClassMethodInvokerHelper.AreTypesCompatible(pars[3], TypeOf<A4>.Value)
                        )
                        {
                            method = info;
                            _delegate = (Func<A1, A2, A3, A4, RT>)Delegate.CreateDelegate(
                                                                    TypeOf<Func<A1, A2, A3, A4, RT>>.Value, /*baseType, */method);
                            break;
                        }
                    }
                }

                cache.TryAdd(key, _delegate);
            }

            return _delegate;
        }

        public static RT Invoke(Type baseType, string methodName, A1 arg1, A2 arg2, A3 arg3, A4 arg4)
        {
            var method = FindMethod(baseType, methodName);
            return method(arg1, arg2, arg3, arg4);
        }

    }   //public static class PrivateStaticClassMethodInvoker<T>

    // ############################################################################
    //
    // ############################################################################

    public static class Reflection
    {
        //[DebuggerStepThrough]
        private static RType GetFieldValue<RType>(Type t, object objInstance, string strField, BindingFlags eFlags)
        {
            FieldInfo m;
            try
            {
                m = t.GetField(strField, eFlags);
                if (m == null)
                {
                    throw new ArgumentException("There is no field '" + strField + "' for type '" + t.ToString() + "'.");
                }

                RType objRet = (RType)m.GetValue(objInstance);
                return objRet;
            }
            catch
            {
                throw;
            }
        }

        //[DebuggerStepThrough]
        public static RType GetInstanceFieldValue<RType, Class>(Class objInstance, string strField)
        {
            return GetInstanceFieldValue<RType>(objInstance.GetType2(), objInstance, strField);
        }

        //[DebuggerStepThrough]
        public static RType GetInstanceFieldValue<RType>(Type t, object objInstance, string strField)
        {
            BindingFlags eFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            return GetFieldValue<RType>(t, objInstance, strField, eFlags);
        } //end of method

        //[DebuggerStepThrough]
        public static RType GetStaticFieldValue<RType>(Type t, string strField)
        {
            BindingFlags eFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            return GetFieldValue<RType>(t, null, strField, eFlags);
        }

        // ############################################################################

        //[DebuggerStepThrough]
        public static void SetInstanceField<RType, Class>(Class objInstance, string strField, RType value)
        {
            SetInstanceField<RType>(objInstance.GetType2(), objInstance, strField, value);
        }

        //[DebuggerStepThrough]
        public static void SetInstanceField<RType>(System.Type t, object objInstance, string strField, RType value)
        {
            BindingFlags eFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            SetField<RType>(t, objInstance, strField, eFlags, value);
        } //end of method

        // ############################################################################

        //[DebuggerStepThrough]
        private static void SetField<RType>(object objInstance, string strField, BindingFlags eFlags, RType value)
        {
            SetField<RType>(objInstance.GetType2(), objInstance, strField, eFlags, value);
        }

        //[DebuggerStepThrough]
        private static void SetField<RType>(System.Type t, object objInstance, string strField, BindingFlags eFlags, RType value)
        {
            FieldInfo m;
            try
            {
                m = t.GetField(strField, eFlags);
                if (m == null)
                {
                    throw new ArgumentException("There is no field '" + strField + "' for type '" + t.ToString() + "'.");
                }

                m.SetValue(objInstance, value);
            }
            catch
            {
                throw;
            }
        }

        // ############################################################################

        /// <summary>
        /// Speeds up GetType calls on value types 
        /// and prevents NullReferenceExceptions on null-valued nullable types. See http://stackoverflow.com/a/194671/193017
        /// </summary>
        //[DebuggerStepThrough]
        public static Type GetType2<T>(this T obj)
        {
            if (
                TypeOf<T>.IsValueType
            )
            {
                return TypeOf<T>.Value;
            }
            else
            {
                return obj.GetType();
            }
        }

    }   //public static class Reflection

    // ############################################################################
    //
    // ############################################################################

    /// <summary>
    /// This class speeds up reflection on types
    /// NB! Use only known final types, not on parent types!
    /// </summary>
    [DebuggerStepThrough]
    public static class TypeOf<T>
    {
        public readonly static Type Value = typeof(T);
        public readonly static Type BaseType = Value.BaseType;
        public readonly static bool IsValueType = Value.IsValueType;
    }

    // ############################################################################
    //
    // ############################################################################

    public static class Creator
    {
        public static T Create<T>(out T obj)
            where T : new()
        {
            return (obj = Creator_<T>.Create());
        }

        public static T Create<T, A1>(out T obj, A1 arg1)
        {
            return (obj = Creator_<T, A1>.Create(arg1));
        }

        public static T Create<T, A1, A2>(out T obj, A1 arg1, A2 arg2)
        {
            return (obj = Creator_<T, A1, A2>.Create(arg1, arg2));
        }

        public static T Create<T, A1, A2, A3>(out T obj, A1 arg1, A2 arg2, A3 arg3)
        {
            return (obj = Creator_<T, A1, A2, A3>.Create(arg1, arg2, arg3));
        }

        public static T Create<T, A1, A2, A3, A4>(out T obj, A1 arg1, A2 arg2, A3 arg3, A4 arg4)
        {
            return (obj = Creator_<T, A1, A2, A3, A4>.Create(arg1, arg2, arg3, arg4));
        }
    }

    // ############################################################################
    //
    // ############################################################################

    //code taken from http://stackoverflow.com/questions/1128073/how-can-i-speed-up-instantiating-a-large-collection-of-objects
    /// <summary>
    /// Creator without new() requirement in the constraints. In practice the new() property is still required
    /// </summary>
    public static class Creator_<T>
    //where T : new()
    {
        public static readonly Func<T> Create =
            Expression.Lambda<Func<T>>(Expression.New(TypeOf<T>.Value)).Compile();
    }

    public static class Creator_<T, A1>
    //where T : new()
    {
        static readonly ParameterExpression para1 = Expression.Parameter(TypeOf<A1>.Value, "a1");

        public static readonly Func<A1, T> Create =
            Expression.Lambda<Func<A1, T>>(Expression.New(TypeOf<T>.Value.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { TypeOf<A1>.Value }, null), para1), para1).Compile();

    }

    public static class Creator_<T, A1, A2>
    //where T : new()
    {
        static readonly ParameterExpression para1 = Expression.Parameter(TypeOf<A1>.Value, "a1");
        static readonly ParameterExpression para2 = Expression.Parameter(TypeOf<A2>.Value, "a2");

        public static readonly Func<A1, A2, T> Create =
            Expression.Lambda<Func<A1, A2, T>>(Expression.New(TypeOf<T>.Value.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { TypeOf<A1>.Value, TypeOf<A2>.Value }, null), para1, para2), para1, para2).Compile();
    }

    public static class Creator_<T, A1, A2, A3>
    //where T : new()
    {
        static readonly ParameterExpression para1 = Expression.Parameter(TypeOf<A1>.Value, "a1");
        static readonly ParameterExpression para2 = Expression.Parameter(TypeOf<A2>.Value, "a2");
        static readonly ParameterExpression para3 = Expression.Parameter(TypeOf<A3>.Value, "a3");

        public static readonly Func<A1, A2, A3, T> Create =
            Expression.Lambda<Func<A1, A2, A3, T>>(Expression.New(TypeOf<T>.Value.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { TypeOf<A1>.Value, TypeOf<A2>.Value, TypeOf<A3>.Value }, null), para1, para2, para3), para1, para2, para3).Compile();
    }

    public static class Creator_<T, A1, A2, A3, A4>
    //where T : new()
    {
        static readonly ParameterExpression para1 = Expression.Parameter(TypeOf<A1>.Value, "a1");
        static readonly ParameterExpression para2 = Expression.Parameter(TypeOf<A2>.Value, "a2");
        static readonly ParameterExpression para3 = Expression.Parameter(TypeOf<A3>.Value, "a3");
        static readonly ParameterExpression para4 = Expression.Parameter(TypeOf<A4>.Value, "a4");

        public static readonly Func<A1, A2, A3, A4, T> Create =
            Expression.Lambda<Func<A1, A2, A3, A4, T>>(Expression.New(TypeOf<T>.Value.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { TypeOf<A1>.Value, TypeOf<A2>.Value, TypeOf<A3>.Value, TypeOf<A4>.Value }, null), para1, para2, para3, para4), para1, para2, para3, para4).Compile();
    }

    public static class Creator_<T, A1, A2, A3, A4, A5>
    //where T : new()
    {
        static readonly ParameterExpression para1 = Expression.Parameter(TypeOf<A1>.Value, "a1");
        static readonly ParameterExpression para2 = Expression.Parameter(TypeOf<A2>.Value, "a2");
        static readonly ParameterExpression para3 = Expression.Parameter(TypeOf<A3>.Value, "a3");
        static readonly ParameterExpression para4 = Expression.Parameter(TypeOf<A4>.Value, "a4");
        static readonly ParameterExpression para5 = Expression.Parameter(TypeOf<A5>.Value, "a5");

        public static readonly Func<A1, A2, A3, A4, A5, T> Create =
            Expression.Lambda<Func<A1, A2, A3, A4, A5, T>>(Expression.New(TypeOf<T>.Value.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { TypeOf<A1>.Value, TypeOf<A2>.Value, TypeOf<A3>.Value, TypeOf<A4>.Value, TypeOf<A5>.Value }, null), para1, para2, para3, para4, para5), para1, para2, para3, para4, para5).Compile();
    }
}
