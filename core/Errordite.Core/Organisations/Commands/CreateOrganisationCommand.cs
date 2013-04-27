﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Web.Security;
using Castle.Core;
using Errordite.Core.Caching.Entities;
using Errordite.Core.Caching.Interceptors;
using Errordite.Core.Domain.Master;
using Errordite.Core.Encryption;
using Errordite.Core.Interfaces;
using Errordite.Core.Applications.Commands;
using Errordite.Core.Caching;
using Errordite.Core.Domain.Organisation;
using Errordite.Core.Extensions;
using System.Linq;
using Errordite.Core.Indexing;
using Errordite.Core.Matching;
using Errordite.Core.Messaging.Commands;
using Errordite.Core.Organisations.Queries;
using Errordite.Core.Session;
using Errordite.Core.Web;

namespace Errordite.Core.Organisations.Commands
{
    [Interceptor(CacheInvalidationInterceptor.IoCName)]
    public class  CreateOrganisationCommand : SessionAccessBase, ICreateOrganisationCommand
    {
        private readonly IGetAvailablePaymentPlansQuery _getAvailablePaymentPlansQuery;
        private readonly IAddApplicationCommand _addApplicationCommand;
        private readonly IEncryptor _encryptor;
        private readonly IGetRavenInstancesQuery _getRavenInstancesQuery;
        private readonly ICreateSQSQueueCommand _createSQSQueueCommand;

        public CreateOrganisationCommand(IGetAvailablePaymentPlansQuery getAvailablePaymentPlansQuery, 
            IAddApplicationCommand addApplicationCommand, 
            IEncryptor encryptor, 
            IGetRavenInstancesQuery getRavenInstancesQuery, 
            ICreateSQSQueueCommand createSqsQueueCommand)
        {
            _getAvailablePaymentPlansQuery = getAvailablePaymentPlansQuery;
            _addApplicationCommand = addApplicationCommand;
            _encryptor = encryptor;
            _getRavenInstancesQuery = getRavenInstancesQuery;
            _createSQSQueueCommand = createSqsQueueCommand;
        }

        public CreateOrganisationResponse Invoke(CreateOrganisationRequest request)
        {
            Trace("Starting...");

            var existingOrganisation = Session.MasterRaven.Query<Organisation, Organisations_Search>().FirstOrDefault(o => o.Name == request.OrganisationName);

            if(existingOrganisation != null)
            {
                return new CreateOrganisationResponse
                {
                    Status = CreateOrganisationStatus.OrganisationExists
                };
            }

            var existingUserMap = Session.MasterRaven.Query<UserOrganisationMapping>().FirstOrDefault(m => m.EmailAddress == request.Email);

            if (existingUserMap != null)
            {
                return new CreateOrganisationResponse
                {
                    Status = CreateOrganisationStatus.UserExists
                };
            }

            var freeTrialPlan = _getAvailablePaymentPlansQuery.Invoke(new GetAvailablePaymentPlansRequest()).Plans.First(p => p.IsTrial && p.IsAvailable);

            var organisation = new Organisation
            {
                Name = request.OrganisationName,
                Status = OrganisationStatus.Active,
                PaymentPlanId = freeTrialPlan.Id,
                CreatedOnUtc = DateTime.UtcNow,
                TimezoneId = request.TimezoneId ?? "UTC",
                PaymentPlan = freeTrialPlan,
                ApiKeySalt = Membership.GeneratePassword(8, 1),
            };

            var ravenInstance = _getRavenInstancesQuery.Invoke(new GetRavenInstancesRequest())
                .RavenInstances
                .FirstOrDefault(r => r.Active) ?? RavenInstance.Master();

            organisation.RavenInstance = ravenInstance;
            organisation.RavenInstanceId = ravenInstance.Id;

            MasterStore(organisation);
            MasterStore(new UserOrganisationMapping{EmailAddress = request.Email, OrganisationId = organisation.Id});

            organisation.ApiKey = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(
                _encryptor.Encrypt("{0}|{1}".FormatWith(organisation.FriendlyId, organisation.ApiKeySalt))));

            Session.SetOrganisation(organisation);
            Session.BootstrapOrganisation(organisation);

            var group = new Group
            {
                Name = request.OrganisationName,
                OrganisationId = organisation.Id
            };

            Store(group);

            var user = new User
            {
                Email = request.Email,
                FirstName = request.FirstName,
                LastName = request.LastName,
                Password = request.Password.Hash(),
                OrganisationId = organisation.Id,
                Role = UserRole.Administrator,
                Status = UserStatus.Active,
                GroupIds = new List<string> { group.Id },
                Organisation = organisation
            };

            Store(user);

            var addApplicationResponse = _addApplicationCommand.Invoke(new AddApplicationRequest
            {
                CurrentUser = user,
                IsActive = true,
                MatchRuleFactoryId = new MethodAndTypeMatchRuleFactory().Id,
                Name = request.OrganisationName,
                NotificationGroups = new List<string> { group.Id },
                UserId = user.Id,
            });

            try
            {
                //create the SQS queue for this organisation
                _createSQSQueueCommand.Invoke(new CreateSQSCommandRequest
                {
                    OrganisationId = organisation.Id
                });

                //add organisation to receive service so we can start receiving errors from the organisations queue
                Session.ReceiveHttpClient.PostJsonAsync("Organisation", organisation);
            }
            catch (Exception e)
            {
                //dont fail the creation of org is service is not running, hope the service starts up soon 
                //when it does it will pick up this org and start processing its errors
                Error(e);
            }

            //TODO: sync indexes
            Session.SynchroniseIndexes<Organisations_Search, Users_Search>();

            return new CreateOrganisationResponse
            {
                OrganisationId = organisation.Id,
                UserId = user.Id,
                ApplicationId = addApplicationResponse.ApplicationId,
            };
        }
    }

    public interface ICreateOrganisationCommand : ICommand<CreateOrganisationRequest, CreateOrganisationResponse>
    { }

    public class CreateOrganisationResponse : CacheInvalidationResponseBase
    {
        public string UserId { get; set; }
        public string OrganisationId { get; set; }
        public CreateOrganisationStatus Status { get; set; }

        public string ApplicationId { get; set; }

        protected override IEnumerable<CacheInvalidationItem> GetCacheInvalidationItems()
        {
            yield return new CacheInvalidationItem(CacheProfiles.Organisations, CacheKeys.Organisations.Key());
        }
    }

    public class CreateOrganisationRequest
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public string OrganisationName { get; set; }
        public string TimezoneId { get; set; }
    }

    public enum CreateOrganisationStatus
    {
        Ok,
        UserExists,
        OrganisationExists
    }
}
