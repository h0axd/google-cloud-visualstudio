﻿// Copyright 2018 Google Inc. All Rights Reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using GoogleCloudExtension.ManageAccounts;
using GoogleCloudExtension.TitleBar;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace GoogleCloudExtensionUnitTests.TitleBar
{
    [TestClass]
    public class TitleBarViewModelTests : ExtensionTestBase
    {
        private TitleBarViewModel _objectUnderTest;

        [TestInitialize]
        public void BeforeEach() => _objectUnderTest = new TitleBarViewModel();

        [TestMethod]
        public void TestGoToAccountManagementCommand_PromptsUser()
        {
            _objectUnderTest.OnGotoAccountManagementCommand.Execute(null);

            PackageMock.Verify(p => p.UserPromptService.PromptUser(It.IsAny<ManageAccountsWindowContent>()));
        }
    }
}
