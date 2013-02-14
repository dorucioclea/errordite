﻿using System;
using System.Collections.Generic;
using CodeTrip.Core.Extensions;
using CodeTrip.Core.Interfaces;
using Errordite.Core.Domain.Error;
using Errordite.Core.Domain.Organisation;
using Errordite.Core.Issues.Queries;
using Errordite.Core.Matching;
using Errordite.Core.Organisations;
using Errordite.Core.Session;

namespace Errordite.Core.Issues.Commands
{
    /// <summary>
    /// Creates an empty issue - i.e. a manual process.
    /// </summary>
    public class AddIssueCommand : SessionAccessBase, IAddIssueCommand
    {
        private readonly IGetIssueWithMatchingRulesQuery _getIssueWithMatchingRulesQuery;

        public AddIssueCommand(IGetIssueWithMatchingRulesQuery getIssueWithMatchingRulesQuery)
        {
            _getIssueWithMatchingRulesQuery = getIssueWithMatchingRulesQuery;
        }

        public AddIssueResponse Invoke(AddIssueRequest request)
        {
            Trace("Starting...");

            var applicationId = Application.GetId(request.ApplicationId);

            var issue = new Issue
            {
                Name = request.Name,
                Rules = request.Rules,
                ApplicationId = applicationId,
                CreatedOnUtc = DateTime.UtcNow,
                LastModifiedUtc = DateTime.UtcNow,
                LastRuleAdjustmentUtc = DateTime.UtcNow,
                UserId = User.GetId(request.AssignedUserId),
                ErrorCount = 0,
                LastErrorUtc = DateTime.UtcNow,
                OrganisationId = Organisation.GetId(request.CurrentUser.OrganisationId),
            };

            var issuesWithSameRules = _getIssueWithMatchingRulesQuery.Invoke(new GetIssueWithMatchingRulesRequest
            {
                IssueToMatch = issue,
            });

            if (issuesWithSameRules.Issue != null)
            {
                return new AddIssueResponse
                {
                    IssueId = issuesWithSameRules.Issue.FriendlyId,
                    Status = AddIssueStatus.SameRulesExist
                };
            }

            Store(issue);
            Store(new IssueHistory
            {
                DateAddedUtc = DateTime.UtcNow,
                UserId = request.CurrentUser.Id,
                Type = HistoryItemType.ManuallyCreated,
                IssueId = issue.Id,
                AssignedToUserId = request.AssignedUserId,
                PreviousStatus = request.Status
            });

            var issueHourlyCount = new IssueHourlyCount
            {
                IssueId = issue.Id,
                Id = "IssueHourlyCount/{0}".FormatWith(issue.FriendlyId)
            };

            issueHourlyCount.Initialise();
            Store(issueHourlyCount);

            Session.AddCommitAction(new RaiseIssueCreatedEvent(issue));

            return new AddIssueResponse
            {
                Status = AddIssueStatus.Ok,
                IssueId = issue.FriendlyId
            };
        }
    }

    public interface IAddIssueCommand : ICommand<AddIssueRequest, AddIssueResponse>
    { }

    public class AddIssueResponse
    {
        public string IssueId { get; set; }
        public AddIssueStatus Status { get; set; }
    }

    public class AddIssueRequest : OrganisationRequestBase
    {
        public string Name { get; set; }
        public string AssignedUserId { get; set; }
        public string ApplicationId { get; set; }
        public IssueStatus Status { get; set; }
        public List<IMatchRule> Rules { get; set; }
    }

    public enum AddIssueStatus
    {
        Ok,
        SameRulesExist
    }
}
