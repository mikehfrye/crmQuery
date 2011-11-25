﻿using System;
using System.Collections;
#if CRM4
using Microsoft.Crm.Sdk.Query;
#else
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
#endif

#if CRM4
namespace Djn.Crm
#else
namespace Djn.Crm5
#endif
{
	/**
	* CrmQuery is a small domain-specific language for building
	*	Microsoft CRM QueryExpressions.
	* 
	* this software is provided under the MIT license. See file LICENSE
	*	for details.
	*	
	* Define CRM4 to compile CrmQuery targeting the CRM4, otherwise
	* targets CRM 2011.
	*/
	public class CrmQuery
	{
		// We use Select() function instead of constructor
		private CrmQuery() { }

		/**
		 * CrmQuery wraps a CRM QueryExpression. Idiomatic usage chains calls 
		 * together, only accessing the Query as the last call in the chain.
		 */
		public QueryExpression Query {
			get { return m_query; }
		}
		private QueryExpression m_query;

		/**
		 * lastAddedLink is a way to let us add filters to a specific
		 * LinkEntity by adding things in order. Allows self-joins.
		 */
		private LinkEntity m_lastAddedLink;

		/**
		 * current expression is set when using Where() and used with
		 * Or() to enable adding new condition to the filter.
		 */
		private FilterExpression m_currentExpression;
		 
		/**
		 * Select serves as the constructor and the start of the 
		 * chain. By Sql convention, accepts the projection list
		 */
#if CRM4
		public static CrmQuery Select( ColumnSetBase in_columns ) {
#else
		public static CrmQuery Select( ColumnSet in_columns ) {
#endif
			QueryExpression query = new QueryExpression();
			query.ColumnSet = in_columns;
			CrmQuery dsl = new CrmQuery();
			dsl.m_query = query;
			return dsl;
		}

		/**
		 * Usage of the default constructor causes all columns to be
		 * returned - avoid this if possible for performance reasons.
		 */
		public static CrmQuery Select() {
#if CRM4
			return Select( new AllColumns() );
#else
			return Select( new ColumnSet(true ) );
#endif
		}

		/**
		 * New in 1.6 - convenience constructor for specifying projection
		 * with variable list of string arguments.
		 */
		public static CrmQuery Select( params string[] in_fields ) {
			return Select( new ColumnSet( in_fields ) );
		}

		/**
		 * From sets the entity type that the query will return
		 */
		public CrmQuery From( string in_entityName ) {
			m_query.EntityName = in_entityName;
			return this;
		}

		/**
		 * Join uses LinkEntity to establish a relation between two entities.
		 * Use Where to add criteria using the 'to' entity.
		 */
		public static LinkEntity JoinExpression( string in_fromEntity, string in_fromField, string in_toEntity, string in_toField ) {
			LinkEntity linkEntity = new LinkEntity();
			linkEntity.LinkFromEntityName = in_fromEntity;
			linkEntity.LinkFromAttributeName = in_fromField;
			linkEntity.LinkToEntityName = in_toEntity;
			linkEntity.LinkToAttributeName = in_toField;
			linkEntity.JoinOperator = JoinOperator.Inner;
			return linkEntity;
		}
		public CrmQuery Join( string in_fromEntity, string in_fromField, string in_toEntity, string in_toField ) {
			LinkEntity linkEntity = JoinExpression( in_fromEntity, in_fromField, in_toEntity, in_toField ) ;
			return Join( in_fromEntity, linkEntity );
		}
		public CrmQuery Join( string in_fromEntity, LinkEntity in_linkEntity ) {
			// TODO: We look at the root query first. Double-think is this the right thing?
			if( m_query.EntityName == in_fromEntity ) {
				m_query.LinkEntities.Add( in_linkEntity );
			}
			else {
				LinkEntity link = FindEntityLink( m_query.LinkEntities, in_fromEntity );
				if( link != null ) {
					link.LinkEntities.Add( in_linkEntity );
				}
			}
			m_lastAddedLink = in_linkEntity;
			return this;
		}

		public static FilterExpression WhereExpression( string in_field, ConditionOperator in_operator, object[] in_values ) {
			FilterExpression filterExpression = new FilterExpression();
			filterExpression.FilterOperator = LogicalOperator.And;

			ConditionExpression ce = new ConditionExpression();
			ce.AttributeName = in_field;
			ce.Operator = in_operator;
#if CRM4
			ce.Values = in_values;
#else
            foreach (object item in in_values) {
                ce.Values.Add(item);
            }
#endif
			filterExpression.Conditions.Add( ce );
			return filterExpression;
		}
		public CrmQuery Where( string in_entity, string in_field, ConditionOperator in_operator, object[] in_values ) {
			FilterExpression filterExpression = CrmQuery.WhereExpression( in_field, in_operator, in_values );
			return Where( in_entity, filterExpression );
		}
		public CrmQuery Where( string in_entity, FilterExpression in_filterExpression ) {
			m_currentExpression = in_filterExpression;

			// TODO: this logic is similar to what is in Join()
			if( m_lastAddedLink != null ) {
				m_lastAddedLink.LinkCriteria.AddFilter( in_filterExpression );
			}
			else if( m_query.EntityName == in_entity ) {
				m_query.Criteria.AddFilter( in_filterExpression );
			}
			else {
				LinkEntity link = FindEntityLink( m_query.LinkEntities, in_entity );
				if( link != null ) {
					link.LinkCriteria.AddFilter( in_filterExpression );
				}
			}
			return this;
		}

		public CrmQuery Or( string in_field, ConditionOperator in_operator, object[] in_values ) {
			if( m_currentExpression != null ) {

				// TODO this logic is repeated in WhereExpression()
				ConditionExpression ce = new ConditionExpression();
				ce.AttributeName = in_field;
				ce.Operator = in_operator;
#if CRM4
				ce.Values = in_values;
#else
				foreach( object item in in_values ) {
					ce.Values.Add( item );
				}
#endif			
				m_currentExpression.AddCondition( ce );
				m_currentExpression.FilterOperator = LogicalOperator.Or;
			}
			else {
				throw new InvalidStateException( "Unable to add 'Or' condition: current filter expression is null" );
			}
			return this;
		}

		/**
		 * Used by Where to figure out which LinkEntity corresponds to the desired
		 * entity we wish to attach the criteria to
		 */
#if CRM4
		private LinkEntity FindEntityLink( ArrayList in_linkEntities, string in_entityName ) {
#else
		private LinkEntity FindEntityLink( DataCollection<LinkEntity> in_linkEntities, string in_entityName ) {
#endif
			foreach( LinkEntity link in in_linkEntities ) {
				FindEntityLink( link.LinkEntities, in_entityName );
				if( link.LinkToEntityName == in_entityName ) {
					return link;
				}
			}
			return null;
		}

		/**
		 * OrderBy adds ordering fields to the query at the toplevel.
		 * 
		 * TODO: for full sql compliance we need to pass array of booleans
		 * since we can specify ascending/descending for each field
		 */
		public CrmQuery OrderBy( string[] in_orderfields, OrderType in_ordertype ) {
			foreach( String orderfield in in_orderfields ) {
				if( ( orderfield != null ) && ( orderfield != "" ) ) {
					m_query.AddOrder( orderfield, in_ordertype );
				}
			}
			return this;
		}

//Added paging ability to keep querry from going insane when doing web developement
        public CrmQuery Page(int maxRecords, int maxPages)
        {
            PagingInfo page = new PagingInfo();

            page.Count = maxRecords;
            page.PageNumber = maxPages;

            m_query.PageInfo = page;


            return this;
        }

	} // class
	class InvalidStateException : Exception {
		public InvalidStateException( string message ) : base( message ) {
		}
	}
} // namespace