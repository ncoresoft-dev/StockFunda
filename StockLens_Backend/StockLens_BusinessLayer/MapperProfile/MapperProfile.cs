using AutoMapper;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Helpers;
using StockLens_DataLayer.Entities;
using StockLens_Infrastructure.ExternalServices.IndianApi.Models;

namespace StockLens_BusinessLayer.MapperProfile
{
    public class MapperProfile : Profile
    {
        public MapperProfile()
        {
            CreateMap<StockNews, StockNewsItemDto>()
                .ForMember(dest => dest.ArticleUrl, opt => opt.MapFrom(src => ArticleUrlNormalizer.Normalize(src.SourceUrl, src.SourceName)));

            CreateMap<Stock, StockDto>();
            CreateMap<Company, CompanyDto>();
            CreateMap<Company, CompanyOverviewDto>()
                .ForMember(dest => dest.KeyPoints, opt => opt.Ignore())
                .ForMember(dest => dest.KeyExecutives, opt => opt.Ignore())
                .ForMember(dest => dest.Source, opt => opt.Ignore());

            CreateMap<IndianApiStandardArticle, StockNews>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.StockId, opt => opt.Ignore())
                .ForMember(dest => dest.Stock, opt => opt.Ignore())
                .ForMember(dest => dest.FetchedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());

            CreateMap<StockShareholding, ShareholdingPeriodDto>()
                .ForMember(dest => dest.Promoter, opt => opt.MapFrom(src => src.PromoterHolding))
                .ForMember(dest => dest.Fii, opt => opt.MapFrom(src => src.FiiHolding))
                .ForMember(dest => dest.Dii, opt => opt.MapFrom(src => src.DiiHolding))
                .ForMember(dest => dest.Government, opt => opt.MapFrom(src => src.GovernmentHolding))
                .ForMember(dest => dest.Public, opt => opt.MapFrom(src => src.PublicHolding))
                .ForMember(dest => dest.Others, opt => opt.MapFrom(src => src.OtherHolding))
                .ForMember(dest => dest.ShareholdersCount, opt => opt.MapFrom(src => src.ShareholdersCount))
                .ForMember(dest => dest.DataAsOf, opt => opt.Ignore());

            CreateMap<StockFinancial, QuarterlyRecordDto>()
                .ForMember(dest => dest.Period, opt => opt.MapFrom(src => src.FiscalYear))
                .ForMember(dest => dest.Sales, opt => opt.MapFrom(src => src.Revenue))
                .ForMember(dest => dest.OpmPercentage, opt => opt.MapFrom(src => src.OperatingProfitMargin));
        }
    }
}
