﻿using System;
using System.Collections.Generic;
using System.Linq;
using CodeTrip.Core.Extensions;
using CodeTrip.Core.Interfaces;
using Errordite.Core.Authorisation;
using Errordite.Core.Domain.Error;
using Errordite.Core.Indexing;
using Errordite.Core.Organisations;
using SessionAccessBase = Errordite.Core.Session.SessionAccessBase;

namespace Errordite.Core.Issues.Queries
{
    public class GetIssueReportDataQuery : SessionAccessBase, IGetIssueReportDataQuery
    {
        private readonly IAuthorisationManager _authorisationManager;

        public GetIssueReportDataQuery(IAuthorisationManager authorisationManager)
        {
            _authorisationManager = authorisationManager;
        }

        public GetIssueReportDataResponse Invoke(GetIssueReportDataRequest request)
        {
            Trace("Starting...");

            var data = new Dictionary<string, object>();
            var hourlyCount = Session.Raven.Load<IssueHourlyCount>("IssueHourlyCount/{0}".FormatWith(request.IssueId.GetFriendlyId()));

            data.Add("ByHour", new
            {
                x = hourlyCount.HourlyCount.Select(h => h.Key.ToString("0")),
                y = hourlyCount.HourlyCount.Select(h => h.Value)
            });

            var dateResults = Query<IssueDailyCount, IssueDailyCount_Search>()
                .Where(i => i.IssueId == Issue.GetId(request.IssueId))
                .Where(i => i.Date >= request.StartDate && i.Date <= request.EndDate)
                .OrderBy(i => i.Date)
                .ToList();

            if (dateResults.Any())
            {
                data.Add("ByDate", new
                {
                    x = dateResults.Select(d => d.Date.ToString("yyyy-MM-dd")),
                    y = dateResults.Select(d => d.Count)
                });
            }
            else
            {
                var range = Enumerable.Range(0, (request.EndDate - request.StartDate).Days + 1).ToList();
                data.Add("ByDate", new
                {
                    x = range.Select(d => request.StartDate.AddDays(d).ToString("yyyy-MM-dd")),
                    y = range.Select(d => 0)
                });
            }

            return new GetIssueReportDataResponse
            {
                Data = data
            };
        }
    }

    public interface IGetIssueReportDataQuery : IQuery<GetIssueReportDataRequest, GetIssueReportDataResponse>
    { }

    public class GetIssueReportDataResponse
    {
        public Dictionary<string, object> Data { get; set; }
    }

    public class GetIssueReportDataRequest : OrganisationRequestBase
    {
        public string IssueId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }
}
