﻿#region License
// Copyright (c) 2013 Chandramouleswaran Ravichandran
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
#endregion
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Practices.Prism.Commands;
using Microsoft.Practices.Prism.Events;
using Microsoft.Practices.Prism.Modularity;
using Microsoft.Practices.Unity;
using Wide.Core.Services;
using Wide.Core.Settings;
using Wide.Core.TextDocument;
using Wide.Interfaces;
using Wide.Interfaces.Events;
using Wide.Interfaces.Services;
using Wide.Interfaces.Settings;
using CommandManager = Wide.Core.Services.CommandManager;
using System.ComponentModel;

namespace Wide.Core
{
    /// <summary>
    /// The Wide Core module - this module does the following things:
    /// 1. Registers <see cref="IOpenFileService" /> - The file service can be used to open a file from a location or from a content ID
    /// 2. Registers <see cref="ICommandManager" /> - The command manager can be used to register commands and reuse the commands in different locations
    /// 3. Registers <see cref="IContentHandlerRegistry" /> - A registry to maintain different content handlers. Each content handler should be able to open a different kind of file/object.
    /// 4. Registers <see cref="IThemeManager" /> - A registry for themes
    /// 5. Registers <see cref="ILoggerService" /> - If not registered already, registers the NLogService which can be used anywhere in the application
    /// 6. Registers <see cref="IToolbarService" /> - The toolbar service used to register multiple toolbars
    /// 7. Registers <see cref="AbstractMenuItem" /> - This acts as the menu service for the application - menus can be added/removed.
    /// 8. Adds an AllFileHandler which can open any file from the system - to override this handler, participating modules can add more handlers to the <see cref="IContentHandlerRegistry" />
    /// </summary>
    [Module(ModuleName = "Wide.Core")]
    internal class CoreModule : IModule
    {
        /// <summary>
        /// The container used in the application
        /// </summary>
        private readonly IUnityContainer _container;
        /// <summary>
        /// The event aggregator
        /// </summary>
        private IEventAggregator _eventAggregator;

        /// <summary>
        /// The constructor of the CoreModule
        /// </summary>
        /// <param name="container">The injected container used in the application</param>
        /// <param name="eventAggregator">The injected event aggregator</param>
        public CoreModule(IUnityContainer container, IEventAggregator eventAggregator)
        {
            _container = container;
            _eventAggregator = eventAggregator;
        }

        /// <summary>
        /// The event aggregator pattern
        /// </summary>
        private IEventAggregator EventAggregator
        {
            get { return _eventAggregator; }
        }

        #region IModule Members
        /// <summary>
        /// The initialize call of the module - this gets called when the container is trying to load the modules.
        /// Register your <see cref="Type"/>s and Commands here
        /// </summary>
        public void Initialize()
        {
            EventAggregator.GetEvent<SplashMessageUpdateEvent>().Publish(new SplashMessageUpdateEvent
                                                                             {Message = "Loading Core Module"});
            _container.RegisterType<TextViewModel>();
            _container.RegisterType<TextModel>();
            _container.RegisterType<TextView>();
            _container.RegisterType<AllFileHandler>();

            _container.RegisterType<IOpenFileService, OpenFileService>(new ContainerControlledLifetimeManager());
            _container.RegisterType<ICommandManager, CommandManager>(new ContainerControlledLifetimeManager());
            _container.RegisterType<IContentHandlerRegistry, ContentHandlerRegistry>(
                new ContainerControlledLifetimeManager());
            _container.RegisterType<IThemeManager, ThemeManager>(new ContainerControlledLifetimeManager());
            _container.RegisterType<IToolbarService, ToolbarService>(new ContainerControlledLifetimeManager());
            _container.RegisterType<AbstractMenuItem, MenuItemViewModel>(new ContainerControlledLifetimeManager(),
                                                                         new InjectionConstructor(
                                                                             new InjectionParameter(typeof (string),
                                                                                                    "$MAIN$"),
                                                                             new InjectionParameter(typeof (int), 1),
                                                                             new InjectionParameter(
                                                                                 typeof (ImageSource), null),
                                                                             new InjectionParameter(typeof (ICommand),
                                                                                                    null),
                                                                             new InjectionParameter(
                                                                                 typeof (KeyGesture), null),
                                                                             new InjectionParameter(typeof (bool), false),
                                                                             new InjectionParameter(
                                                                                 typeof (IUnityContainer), _container)));
            _container.RegisterType<ToolbarViewModel>(
                new InjectionConstructor(new InjectionParameter(typeof (string), "$MAIN$"),
                                         new InjectionParameter(typeof (int), 1),
                                         new InjectionParameter(typeof (ImageSource), null),
                                         new InjectionParameter(typeof (ICommand), null),
                                         new InjectionParameter(typeof (bool), false),
                                         new InjectionParameter(typeof (IUnityContainer), _container)));

            _container.RegisterType<ISettingsManager, SettingsManager>(new ContainerControlledLifetimeManager());

            AppCommands();

            //Try resolving a workspace
            try
            {
                _container.Resolve<AbstractWorkspace>();
            }
            catch
            {
                _container.RegisterType<AbstractWorkspace, Workspace>(new ContainerControlledLifetimeManager());
            }
            
            // Try resolving a logger service - if not found, then register the NLog service
            try
            {
                _container.Resolve<ILoggerService>();
            }
            catch
            {
                _container.RegisterType<ILoggerService, NLogService>(new ContainerControlledLifetimeManager());
            }

            //Register a default file opener
            var registry = _container.Resolve<IContentHandlerRegistry>();
            registry.Register(_container.Resolve<AllFileHandler>());
        }

        #endregion

        /// <summary>
        /// The AppCommands registered by the Core Module
        /// </summary>
        private void AppCommands()
        {
            var manager = _container.Resolve<ICommandManager>();

            //TODO: Check if you can hook up to the Workspace.ActiveDocument.CloseCommand
            var closeCommand = new DelegateCommand<CancelEventArgs>(CloseDocument, CanExecuteCloseDocument);
            manager.RegisterCommand("CLOSE", closeCommand);

            var newCommand = new DelegateCommand(NewDocument, CanExecuteNewCommand);
            manager.RegisterCommand("NEW", newCommand);
        }

        #region Commands
        /// <summary>
        /// Can the close command execute? Checks if there is an ActiveDocument - if present, returns true.
        /// </summary>
        /// <param name="e">The <see cref="CancelEventArgs"/> instance containing the event data.</param>
        /// <returns><c>true</c> if this instance can execute close document; otherwise, <c>false</c>.</returns>
        private bool CanExecuteCloseDocument(CancelEventArgs e)
        {
            IWorkspace workspace = _container.Resolve<AbstractWorkspace>();
            return workspace.ActiveDocument != null;
        }

        /// <summary>
        /// CloseDocument method that gets called when the Close command gets executed.
        /// </summary>
        private void CloseDocument(CancelEventArgs e)
        {
            IWorkspace workspace = _container.Resolve<AbstractWorkspace>();
            ILoggerService logger = _container.Resolve<ILoggerService>();
            if (workspace.ActiveDocument.Model.IsDirty)
            {
                //means the document is dirty - show a message box and then handle based on the user's selection
                var res = MessageBox.Show(string.Format("Save changes for document '{0}'?", workspace.ActiveDocument.Title), "Are you sure?", MessageBoxButton.YesNoCancel);

                //Pressed Yes
                if (res == MessageBoxResult.Yes)
                {
                    if (!workspace.ActiveDocument.Handler.SaveContent(workspace.ActiveDocument))
                    {
                        //Failed to save - return cancel
                        res = MessageBoxResult.Cancel;
                        
                        //Cancel was pressed - so, we cant close
                        if (e != null)
                        {
                            e.Cancel = true;
                        }
                        return;
                    }
                }

                //Pressed Cancel
                if (res == MessageBoxResult.Cancel)
                {
                    //Cancel was pressed - so, we cant close
                    if (e != null)
                    {
                        e.Cancel = true;
                    }
                    return;
                }
            }

            if (e == null)
            {
                logger.Log("Closing document " + workspace.ActiveDocument.Model.Location, LogCategory.Info, LogPriority.None);
                workspace.Documents.Remove(workspace.ActiveDocument);
            }
            else
            {
                // If the location is not there - then we can remove it.
                // This can happen when on clicking "No" in the popup and we still want to quit
                if (workspace.ActiveDocument.Model.Location == null)
                {
                    workspace.Documents.Remove(workspace.ActiveDocument);
                }
            }
        }

        private bool CanExecuteNewCommand()
        {
            return true;
        }

        private void NewDocument()
        {
            var contentHandler = _container.Resolve<IContentHandlerRegistry>() as ContentHandlerRegistry;
            var workspace = _container.Resolve<AbstractWorkspace>();

            if(contentHandler != null)
            {
                if(contentHandler.ContentHandlers.Count != 1)
                {
                    foreach (var handler in contentHandler.ContentHandlers)
                    {
                        //TODO: This is the place where we want to show a window and make the end user select a type of file
                        workspace.Documents.Add(handler.NewContent(null));
                    }
                }
                else
                {
                    var openValue = contentHandler.ContentHandlers[0].NewContent(null);
                    workspace.Documents.Add(openValue);

                    //Make it the active document
                    workspace.ActiveDocument = openValue;
                }
            }
        }

        #endregion
    }
}