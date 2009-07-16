﻿/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Microsoft Public License. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the  Microsoft Public License, please send an email to 
 * dlr@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Microsoft Public License.
 *
 * You must not remove this notice, or any other, from this software.
 *
 *
 * ***************************************************************************/

using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.CompilerServices;
using Microsoft.Scripting.Runtime;
using IronPython.Runtime.Binding;
using System.Reflection;
using IronPython.Runtime.Operations;
using System.Linq.Expressions;
using Microsoft.Scripting.Actions;

namespace IronPython.Runtime.Types {
    public partial class PythonType {

        #region IFastInvokable Members

        /// <summary>
        /// Implements fast binding for user defined types.  This ensures that common highly dynamic
        /// scenarios will run fast (for instance creating new types repeatedly and only creating a limited
        /// number of instances of them).  It also gives better code sharing amongst different subclasses
        /// of the same types and improved startup time due to reduced code generation.
        /// </summary>
        FastBindResult<T> IFastInvokable.MakeInvokeBinding<T>(CallSite<T> site, PythonInvokeBinder binder, CodeContext context, object[] args) {
            PythonTypeSlot del;
            if (IsSystemType ||                                         // limited numbers of these, just generate optimal code
                IsMixedNewStyleOldStyle() ||                            // old-style classes shouldn't be commonly used
                TryResolveSlot(context, Symbols.Unassign, out del) ||   // same w/ finalizers.
                args.Length > 5 ||                                     // and we only generate optimal code for a small number of calls.
                GetType() != typeof(PythonType)) {                      // and we don't handle meta classes yet (they could override __call__)...
                return new FastBindResult<T>();
            }

            ParameterInfo[] paramTypes = typeof(T).GetMethod("Invoke").GetParameters();
            if (paramTypes[2].ParameterType != typeof(object)) {
                // strongly typed to PythonType, we don't optimize these sites yet.
                return new FastBindResult<T>();
            }

            Type[] genTypeArgs = new Type[paramTypes.Length - 3]; // minus CallSite, CodeContext, type
            for (int i = 0; i < paramTypes.Length - 3; i++) {
                genTypeArgs[i] = paramTypes[i + 3].ParameterType;
            }

            FastBindingBuilderBase fastBindBuilder;
            if (genTypeArgs.Length == 0) {
                fastBindBuilder = new FastBindingBuilder(context, this, binder, typeof(T), genTypeArgs);
            } else {
                Type baseType;
                switch (genTypeArgs.Length) {
                    #region Generated Python Fast Type Caller Switch

                    // *** BEGIN GENERATED CODE ***
                    // generated by function: gen_fast_type_caller_switch from: generate_calls.py

                    case 1: baseType = typeof(FastBindingBuilder<>); break;
                    case 2: baseType = typeof(FastBindingBuilder<,>); break;
                    case 3: baseType = typeof(FastBindingBuilder<,,>); break;
                    case 4: baseType = typeof(FastBindingBuilder<,,,>); break;
                    case 5: baseType = typeof(FastBindingBuilder<,,,,>); break;

                    // *** END GENERATED CODE ***

                    #endregion
                    default:
                        throw new NotImplementedException();
                }

                fastBindBuilder = (FastBindingBuilderBase)Activator.CreateInstance(baseType.MakeGenericType(genTypeArgs), context, this, binder, typeof(T), genTypeArgs);
            }

            return new FastBindResult<T>((T)(object)fastBindBuilder.MakeBindingResult(), true);
        }

        /// <summary>
        /// Base class for doing fast type invoke binding.  Subclasses are created using
        /// reflection once during the binding.  The subclasses can then proceed to do
        /// the binding w/o using reflection.  Otherwise we'd have lots more reflection
        /// calls which would slow the binding up.
        /// </summary>
        abstract class FastBindingBuilderBase {
            private readonly CodeContext _context;
            private readonly PythonInvokeBinder _binder;
            private readonly PythonType _type;
            private readonly Type _siteType;
            private readonly Type[] _genTypeArgs;

            public FastBindingBuilderBase(CodeContext context, PythonType type, PythonInvokeBinder binder, Type siteType, Type[] genTypeArgs) {
                _context = context;
                _type = type;
                // binder is used for nested call sites which have 1 extra argument (the type passed to __new__, or the instance passed to __init__).
                _binder = binder.Context.Invoke(binder.Signature.InsertArgument(Argument.Simple));
                _siteType = siteType;
                _genTypeArgs = genTypeArgs;
            }

            public virtual Delegate MakeBindingResult() {
                int version = _type.Version;

                PythonTypeSlot init, newInst;
                _type.TryResolveSlot(_context, Symbols.NewInst, out newInst);
                _type.TryResolveSlot(_context, Symbols.Init, out init);

                Delegate newDlg;
                Delegate initDlg;
                if (init == InstanceOps.Init) {
                    if (_genTypeArgs.Length > 0 & newInst == InstanceOps.New) {
                        // init needs to report an error, we don't optimize for the error case.
                        return null;
                    }
                    initDlg = null;
                } else if (init is PythonFunction) {
                    initDlg = GetNewOrInitSiteDelegate(_binder, init);
                } else {
                    // odd slot in __init__, we don't handle this either.
                    return null;
                }

                if (newInst == InstanceOps.New) {
                    newDlg = GetOrCreateFastNew();
                } else if (newInst.GetType() == typeof(staticmethod) && ((staticmethod)newInst)._func is PythonFunction) {
                    newDlg = GetNewOrInitSiteDelegate(_binder, ((staticmethod)newInst)._func);
                } else {
                    // odd slot in __new__, we don't handle this.
                    newDlg = null;
                }

                if (newDlg == null) {
                    // can't optimize this method because we have no fast-new
                    return null;
                }

                return MakeDelegate(version, newDlg, initDlg);
            }

            /// <summary>
            /// Gets or creates delegate for calling the constructor function.
            /// </summary>
            private Delegate GetOrCreateFastNew() {
                Delegate newDlg;
                lock (_fastBindCtors) {
                    Dictionary<Type, Delegate> fastBindCtors;
                    if (!_fastBindCtors.TryGetValue(_type.UnderlyingSystemType, out fastBindCtors)) {
                        fastBindCtors = _fastBindCtors[_type.UnderlyingSystemType] = new Dictionary<Type, Delegate>();
                    } else if (fastBindCtors.TryGetValue(_siteType, out newDlg)) {
                        return newDlg;
                    }

                    ConstructorInfo[] cis = _type.UnderlyingSystemType.GetConstructors();
                    if (cis.Length == 1 && cis[0].GetParameters().Length == 1 && cis[0].GetParameters()[0].ParameterType == typeof(PythonType)) {
                        ParameterExpression contextArg = Expression.Parameter(typeof(CodeContext));
                        ParameterExpression type = Expression.Parameter(typeof(object));
                        ParameterExpression[] args = new ParameterExpression[_genTypeArgs.Length + 2]; // CodeContext, callable func
                        args[0] = contextArg;
                        args[1] = type;

                        for (int i = 0; i < _genTypeArgs.Length; i++) {
                            // skip CallSite, CodeContext, target function when getting args.
                            args[i + 2] = Expression.Parameter(_genTypeArgs[i]);
                        }

                        newDlg = Expression.Lambda(
                            Expression.Convert(
                                Expression.New(cis[0], Expression.Convert(type, typeof(PythonType))),
                                typeof(object)
                            ),
                            args
                        ).Compile();

                        fastBindCtors[_siteType] = newDlg;
                    } else {
                        // we have overloads we could dispatch to, generate optimal code.
                        return null;
                    }
                }

                return newDlg;
            }

            protected abstract Delegate GetNewOrInitSiteDelegate(PythonInvokeBinder binder, object func);
            protected abstract Delegate MakeDelegate(int version, Delegate newDlg, Delegate initDlg);
        }

        class FastBindingBuilder : FastBindingBuilderBase {
            public FastBindingBuilder(CodeContext context, PythonType type, PythonInvokeBinder binder, Type siteType, Type[] genTypeArgs) :
                base(context, type, binder, siteType, genTypeArgs) {
            }

            protected override Delegate GetNewOrInitSiteDelegate(PythonInvokeBinder binder, object func) {
                return new Func<CodeContext, object, object>(new NewInitSite(binder, func).Call);
            }

            protected override Delegate MakeDelegate(int version, Delegate newDlg, Delegate initDlg) {
                return new Func<CallSite, CodeContext, object, object>(
                            new FastTypeSite(
                                version,
                                (Func<CodeContext, object, object>)newDlg,
                                (Func<CodeContext, object, object>)initDlg).CallTarget
                );
            }
        }

        class FastTypeSite {
            private readonly int _version;
            private readonly Func<CodeContext, object, object> _new;
            private readonly Func<CodeContext, object, object> _init;

            public FastTypeSite(int version, Func<CodeContext, object, object> @new, Func<CodeContext, object, object> init) {
                _version = version;
                _new = @new;
                _init = init;
            }

            public object CallTarget(CallSite site, CodeContext context, object type) {
                PythonType pt = type as PythonType;
                if (pt != null && pt.Version == _version) {
                    object res = _new(context, type);
                    if (_init != null && res != null && res.GetType() == pt.UnderlyingSystemType) {
                        _init(context, res);
                    }

                    return res;
                }

                return ((CallSite<Func<CallSite, CodeContext, object, object>>)site).Update(site, context, type);
            }
        }

        class NewInitSite {
            private readonly CallSite<Func<CallSite, CodeContext, object, object, object>> _site;
            private readonly object _target;

            public NewInitSite(PythonInvokeBinder binder, object target) {
                _site = CallSite<Func<CallSite, CodeContext, object, object, object>>.Create(binder);
                _target = target;
            }

            public object Call(CodeContext context, object typeOrInstance) {
                return _site.Target(_site, context, _target, typeOrInstance);
            }
        }

        #endregion

        #region Generated Python Fast Type Callers

        // *** BEGIN GENERATED CODE ***
        // generated by function: gen_fast_type_callers from: generate_calls.py


        class FastBindingBuilder<T0> : FastBindingBuilderBase {
            public FastBindingBuilder(CodeContext context, PythonType type, PythonInvokeBinder binder, Type siteType, Type[] genTypeArgs) :
                base(context, type, binder, siteType, genTypeArgs) {
            }

            protected override Delegate GetNewOrInitSiteDelegate(PythonInvokeBinder binder, object func) {
                return new Func<CodeContext, object, T0, object>(new NewInitSite<T0>(binder, func).Call);
            }

            protected override Delegate MakeDelegate(int version, Delegate newDlg, Delegate initDlg) {
                return new Func<CallSite, CodeContext, object, T0, object>(
                            new FastTypeSite<T0>
                                (version, 
                                (Func<CodeContext, object, T0, object>)newDlg, 
                                (Func<CodeContext, object, T0, object>)initDlg).CallTarget
                );
            }
        }

        class FastTypeSite<T0> {
            private readonly int _version;
            private readonly Func<CodeContext, object, T0, object> _new;
            private readonly Func<CodeContext, object, T0, object> _init;

            public FastTypeSite(int version, Func<CodeContext, object, T0, object> @new, Func<CodeContext, object, T0, object> init) {
                _version = version;
                _new = @new;
                _init = init;
            }

            public object CallTarget(CallSite site, CodeContext context, object type, T0 arg0) {
                PythonType pt = type as PythonType;
                if (pt != null && pt.Version == _version) {
                    object res = _new(context, type, arg0);
                    if (_init != null && res != null && res.GetType() == pt.UnderlyingSystemType) {
                        _init(context, res, arg0);
                    }

                    return res;
                }

                return ((CallSite<Func<CallSite, CodeContext, object, T0, object>>)site).Update(site, context, type, arg0);
            }
        }

        class NewInitSite<T0> {
            private readonly CallSite<Func<CallSite, CodeContext, object, object, T0, object>> _site;
            private readonly object _target;

            public NewInitSite(PythonInvokeBinder binder, object target) {
                _site = CallSite<Func<CallSite, CodeContext, object, object, T0, object>>.Create(binder);
                _target = target;
            }

            public object Call(CodeContext context, object typeOrInstance, T0 arg0) {
                return _site.Target(_site, context, _target, typeOrInstance, arg0);
            }
        }


        class FastBindingBuilder<T0, T1> : FastBindingBuilderBase {
            public FastBindingBuilder(CodeContext context, PythonType type, PythonInvokeBinder binder, Type siteType, Type[] genTypeArgs) :
                base(context, type, binder, siteType, genTypeArgs) {
            }

            protected override Delegate GetNewOrInitSiteDelegate(PythonInvokeBinder binder, object func) {
                return new Func<CodeContext, object, T0, T1, object>(new NewInitSite<T0, T1>(binder, func).Call);
            }

            protected override Delegate MakeDelegate(int version, Delegate newDlg, Delegate initDlg) {
                return new Func<CallSite, CodeContext, object, T0, T1, object>(
                            new FastTypeSite<T0, T1>
                                (version, 
                                (Func<CodeContext, object, T0, T1, object>)newDlg, 
                                (Func<CodeContext, object, T0, T1, object>)initDlg).CallTarget
                );
            }
        }

        class FastTypeSite<T0, T1> {
            private readonly int _version;
            private readonly Func<CodeContext, object, T0, T1, object> _new;
            private readonly Func<CodeContext, object, T0, T1, object> _init;

            public FastTypeSite(int version, Func<CodeContext, object, T0, T1, object> @new, Func<CodeContext, object, T0, T1, object> init) {
                _version = version;
                _new = @new;
                _init = init;
            }

            public object CallTarget(CallSite site, CodeContext context, object type, T0 arg0, T1 arg1) {
                PythonType pt = type as PythonType;
                if (pt != null && pt.Version == _version) {
                    object res = _new(context, type, arg0, arg1);
                    if (_init != null && res != null && res.GetType() == pt.UnderlyingSystemType) {
                        _init(context, res, arg0, arg1);
                    }

                    return res;
                }

                return ((CallSite<Func<CallSite, CodeContext, object, T0, T1, object>>)site).Update(site, context, type, arg0, arg1);
            }
        }

        class NewInitSite<T0, T1> {
            private readonly CallSite<Func<CallSite, CodeContext, object, object, T0, T1, object>> _site;
            private readonly object _target;

            public NewInitSite(PythonInvokeBinder binder, object target) {
                _site = CallSite<Func<CallSite, CodeContext, object, object, T0, T1, object>>.Create(binder);
                _target = target;
            }

            public object Call(CodeContext context, object typeOrInstance, T0 arg0, T1 arg1) {
                return _site.Target(_site, context, _target, typeOrInstance, arg0, arg1);
            }
        }


        class FastBindingBuilder<T0, T1, T2> : FastBindingBuilderBase {
            public FastBindingBuilder(CodeContext context, PythonType type, PythonInvokeBinder binder, Type siteType, Type[] genTypeArgs) :
                base(context, type, binder, siteType, genTypeArgs) {
            }

            protected override Delegate GetNewOrInitSiteDelegate(PythonInvokeBinder binder, object func) {
                return new Func<CodeContext, object, T0, T1, T2, object>(new NewInitSite<T0, T1, T2>(binder, func).Call);
            }

            protected override Delegate MakeDelegate(int version, Delegate newDlg, Delegate initDlg) {
                return new Func<CallSite, CodeContext, object, T0, T1, T2, object>(
                            new FastTypeSite<T0, T1, T2>
                                (version, 
                                (Func<CodeContext, object, T0, T1, T2, object>)newDlg, 
                                (Func<CodeContext, object, T0, T1, T2, object>)initDlg).CallTarget
                );
            }
        }

        class FastTypeSite<T0, T1, T2> {
            private readonly int _version;
            private readonly Func<CodeContext, object, T0, T1, T2, object> _new;
            private readonly Func<CodeContext, object, T0, T1, T2, object> _init;

            public FastTypeSite(int version, Func<CodeContext, object, T0, T1, T2, object> @new, Func<CodeContext, object, T0, T1, T2, object> init) {
                _version = version;
                _new = @new;
                _init = init;
            }

            public object CallTarget(CallSite site, CodeContext context, object type, T0 arg0, T1 arg1, T2 arg2) {
                PythonType pt = type as PythonType;
                if (pt != null && pt.Version == _version) {
                    object res = _new(context, type, arg0, arg1, arg2);
                    if (_init != null && res != null && res.GetType() == pt.UnderlyingSystemType) {
                        _init(context, res, arg0, arg1, arg2);
                    }

                    return res;
                }

                return ((CallSite<Func<CallSite, CodeContext, object, T0, T1, T2, object>>)site).Update(site, context, type, arg0, arg1, arg2);
            }
        }

        class NewInitSite<T0, T1, T2> {
            private readonly CallSite<Func<CallSite, CodeContext, object, object, T0, T1, T2, object>> _site;
            private readonly object _target;

            public NewInitSite(PythonInvokeBinder binder, object target) {
                _site = CallSite<Func<CallSite, CodeContext, object, object, T0, T1, T2, object>>.Create(binder);
                _target = target;
            }

            public object Call(CodeContext context, object typeOrInstance, T0 arg0, T1 arg1, T2 arg2) {
                return _site.Target(_site, context, _target, typeOrInstance, arg0, arg1, arg2);
            }
        }


        class FastBindingBuilder<T0, T1, T2, T3> : FastBindingBuilderBase {
            public FastBindingBuilder(CodeContext context, PythonType type, PythonInvokeBinder binder, Type siteType, Type[] genTypeArgs) :
                base(context, type, binder, siteType, genTypeArgs) {
            }

            protected override Delegate GetNewOrInitSiteDelegate(PythonInvokeBinder binder, object func) {
                return new Func<CodeContext, object, T0, T1, T2, T3, object>(new NewInitSite<T0, T1, T2, T3>(binder, func).Call);
            }

            protected override Delegate MakeDelegate(int version, Delegate newDlg, Delegate initDlg) {
                return new Func<CallSite, CodeContext, object, T0, T1, T2, T3, object>(
                            new FastTypeSite<T0, T1, T2, T3>
                                (version, 
                                (Func<CodeContext, object, T0, T1, T2, T3, object>)newDlg, 
                                (Func<CodeContext, object, T0, T1, T2, T3, object>)initDlg).CallTarget
                );
            }
        }

        class FastTypeSite<T0, T1, T2, T3> {
            private readonly int _version;
            private readonly Func<CodeContext, object, T0, T1, T2, T3, object> _new;
            private readonly Func<CodeContext, object, T0, T1, T2, T3, object> _init;

            public FastTypeSite(int version, Func<CodeContext, object, T0, T1, T2, T3, object> @new, Func<CodeContext, object, T0, T1, T2, T3, object> init) {
                _version = version;
                _new = @new;
                _init = init;
            }

            public object CallTarget(CallSite site, CodeContext context, object type, T0 arg0, T1 arg1, T2 arg2, T3 arg3) {
                PythonType pt = type as PythonType;
                if (pt != null && pt.Version == _version) {
                    object res = _new(context, type, arg0, arg1, arg2, arg3);
                    if (_init != null && res != null && res.GetType() == pt.UnderlyingSystemType) {
                        _init(context, res, arg0, arg1, arg2, arg3);
                    }

                    return res;
                }

                return ((CallSite<Func<CallSite, CodeContext, object, T0, T1, T2, T3, object>>)site).Update(site, context, type, arg0, arg1, arg2, arg3);
            }
        }

        class NewInitSite<T0, T1, T2, T3> {
            private readonly CallSite<Func<CallSite, CodeContext, object, object, T0, T1, T2, T3, object>> _site;
            private readonly object _target;

            public NewInitSite(PythonInvokeBinder binder, object target) {
                _site = CallSite<Func<CallSite, CodeContext, object, object, T0, T1, T2, T3, object>>.Create(binder);
                _target = target;
            }

            public object Call(CodeContext context, object typeOrInstance, T0 arg0, T1 arg1, T2 arg2, T3 arg3) {
                return _site.Target(_site, context, _target, typeOrInstance, arg0, arg1, arg2, arg3);
            }
        }


        class FastBindingBuilder<T0, T1, T2, T3, T4> : FastBindingBuilderBase {
            public FastBindingBuilder(CodeContext context, PythonType type, PythonInvokeBinder binder, Type siteType, Type[] genTypeArgs) :
                base(context, type, binder, siteType, genTypeArgs) {
            }

            protected override Delegate GetNewOrInitSiteDelegate(PythonInvokeBinder binder, object func) {
                return new Func<CodeContext, object, T0, T1, T2, T3, T4, object>(new NewInitSite<T0, T1, T2, T3, T4>(binder, func).Call);
            }

            protected override Delegate MakeDelegate(int version, Delegate newDlg, Delegate initDlg) {
                return new Func<CallSite, CodeContext, object, T0, T1, T2, T3, T4, object>(
                            new FastTypeSite<T0, T1, T2, T3, T4>
                                (version, 
                                (Func<CodeContext, object, T0, T1, T2, T3, T4, object>)newDlg, 
                                (Func<CodeContext, object, T0, T1, T2, T3, T4, object>)initDlg).CallTarget
                );
            }
        }

        class FastTypeSite<T0, T1, T2, T3, T4> {
            private readonly int _version;
            private readonly Func<CodeContext, object, T0, T1, T2, T3, T4, object> _new;
            private readonly Func<CodeContext, object, T0, T1, T2, T3, T4, object> _init;

            public FastTypeSite(int version, Func<CodeContext, object, T0, T1, T2, T3, T4, object> @new, Func<CodeContext, object, T0, T1, T2, T3, T4, object> init) {
                _version = version;
                _new = @new;
                _init = init;
            }

            public object CallTarget(CallSite site, CodeContext context, object type, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4) {
                PythonType pt = type as PythonType;
                if (pt != null && pt.Version == _version) {
                    object res = _new(context, type, arg0, arg1, arg2, arg3, arg4);
                    if (_init != null && res != null && res.GetType() == pt.UnderlyingSystemType) {
                        _init(context, res, arg0, arg1, arg2, arg3, arg4);
                    }

                    return res;
                }

                return ((CallSite<Func<CallSite, CodeContext, object, T0, T1, T2, T3, T4, object>>)site).Update(site, context, type, arg0, arg1, arg2, arg3, arg4);
            }
        }

        class NewInitSite<T0, T1, T2, T3, T4> {
            private readonly CallSite<Func<CallSite, CodeContext, object, object, T0, T1, T2, T3, T4, object>> _site;
            private readonly object _target;

            public NewInitSite(PythonInvokeBinder binder, object target) {
                _site = CallSite<Func<CallSite, CodeContext, object, object, T0, T1, T2, T3, T4, object>>.Create(binder);
                _target = target;
            }

            public object Call(CodeContext context, object typeOrInstance, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4) {
                return _site.Target(_site, context, _target, typeOrInstance, arg0, arg1, arg2, arg3, arg4);
            }
        }


        // *** END GENERATED CODE ***

        #endregion

    }
}
