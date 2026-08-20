# PostgreSQL Historian Schema

The application now follows this target order:

1. `ID` — `bigint`
2. `rec_Turbidity_NTU` — `numeric`
3. `rec_FreeChlorine_ppm` — `numeric`
4. `rec_AcidBase_pH` — `numeric`
5. `rec_FlwMtr_A_Flowrate_m3p` — `numeric`
6. `rec_FlwMtr_A_Tot_m3` — `numeric`
7. `rec_FlwMtr_B_Flowrate_m3p` — `numeric`
8. `rec_FlwMtr_B_Tot_m3` — `numeric`
9. `rec_Pressure_A` — `numeric`
10. `rec_Pressure_B` — `numeric`
11. `rec_DATE` — `text`
12. `rec_TS` — `timestamp without time zone`

## Ewon source mapping

| Ewon TXT | PostgreSQL |
|---|---|
| AI1_Turbidity | rec_Turbidity_NTU |
| AI2_FreeChlorine | rec_FreeChlorine_ppm |
| AI3_pH | rec_AcidBase_pH |
| FM_Left_FlowRate | rec_FlwMtr_A_Flowrate_m3p |
| FM_Left_Tot1_Log | rec_FlwMtr_A_Tot_m3 |
| FM_Right_FlowRate | rec_FlwMtr_B_Flowrate_m3p |
| FM_Right_Tot1_Log | rec_FlwMtr_B_Tot_m3 |
| Pressure_A_psi | rec_Pressure_A |
| Pressure_B_psi | rec_Pressure_B |
| TimeStr | rec_DATE |
| parsed TimeStr | rec_TS |

All numeric measurements are rounded to two decimal places before preview and
again at the database insertion boundary.

`ID` is not sourced from Ewon. PostgreSQL must generate it automatically.

For compatibility with the earlier pgAdmin screenshot, the injector also accepts
`rec_FlwMtr_A_Flowrate_m3ph` and `rec_FlwMtr_B_Flowrate_m3ph` as database aliases,
but the application preview uses the requested `m3p` names.
